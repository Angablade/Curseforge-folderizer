using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;
using PuppeteerSharp;

class Program
{
    static async Task<string> Main(string[] args)
    {
        Console.WriteLine("Starting Modpack Processing...");

        string zipFilePath = args[0];

        if(zipFilePath == null) {
            return ("You forgot to enter something.");
        }
        else if(IsWebUrl(zipFilePath))
        {
            zipFilePath = await DownloadModpack(zipFilePath);
        }

        string extractionDirectory = Path.Combine(Directory.GetCurrentDirectory(), ".modpack");
        Directory.CreateDirectory(extractionDirectory);

        string outputFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory.ToString(), "ModpackOutput");
        Directory.CreateDirectory(outputFolderPath);
        Console.WriteLine($"Created output folder: {outputFolderPath}");


        Console.WriteLine($"Created extraction directory: {extractionDirectory}");
        ExtractFilesFromZip(zipFilePath, extractionDirectory, outputFolderPath, "manifest.json", "modlist.html");
        Console.WriteLine("Files extracted successfully.");

            

        ModpackData modpackData = ReadManifestJson(Path.Combine(extractionDirectory, "manifest.json"));
        string modlistHtmlPath = Path.Combine(extractionDirectory, "modlist.html");
        string[] modLinks = ReadModListLinks(modlistHtmlPath);

        if (modLinks.Length > 0)
        {
            for (int i = 0; i < Math.Min(modLinks.Length, modpackData.files.Count); i++)
            {
                modpackData.files[i].link = modLinks[i];
            }
        }

        string outputFilePath = Path.Combine(outputFolderPath, $"{SanitizeName(modpackData.name)}_{modpackData.version}.txt");
        Console.WriteLine($"Writing modpack info to: {outputFilePath}");
        WriteModpackInfo(outputFilePath, modpackData);

        Console.WriteLine("Processing mod links and downloading files...");
        await ProcessLinksAndDownloadAsync(modpackData.files, outputFolderPath);

        Console.WriteLine($"Deleting extraction directory: {extractionDirectory}");
        Directory.Delete(extractionDirectory, true);
        Console.WriteLine("Processing completed successfully.");
        
        return "Task Finished";
    }

    static void ExtractFilesFromZip(string zipFilePath, string extractionDirectory, string outputFolderPath, params string[] fileNames)
    {
        try
        {
            Directory.CreateDirectory(extractionDirectory ?? string.Empty);
            using (ZipArchive zipArchive = ZipFile.OpenRead(zipFilePath))
            {
                Parallel.ForEach(zipArchive.Entries, (entry, state) =>
                {
                    if (entry.Length > 3) { 
                         Console.WriteLine("Reading file: " + entry);
                        string entryName = DetectAndConvertEntryName(zipArchive, entry.FullName);
                        entryName = entryName.Substring(entryName.IndexOf("/") + 1).Replace("/".ToArray()[0], Path.DirectorySeparatorChar);
                    
                        if (fileNames.Contains(entryName))
                        {
                            string filePath = Path.Combine(extractionDirectory, entryName);
                            string fileath = Path.Combine(outputFolderPath, entryName);
                            Directory.CreateDirectory(Path.GetDirectoryName(fileath) ?? string.Empty);
                            Console.WriteLine("Extracting: " + entryName);
                            entry.ExtractToFile(filePath, true);
                            entry.ExtractToFile(fileath, true);
                        }
                        else
                        {
                            string filePath = Path.Combine(outputFolderPath, entryName);
                            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? string.Empty);
                            Console.WriteLine("Extracting: " + entryName);
                            entry.ExtractToFile(filePath, true);
                        }
                    }
                   
                });

            }
        }catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    private static string DetectAndConvertEntryName(ZipArchive zipArchive, string fileName)
    {
        foreach (ZipArchiveEntry entry in zipArchive.Entries)
        {
            try
            {
                Encoding.GetEncoding(entry.FullName);
                return fileName;
            }
            catch (Exception)
            {
            }
        }

        return Encoding.UTF8.GetString(Encoding.Default.GetBytes(fileName));
    }
    static ModpackData ReadManifestJson(string manifestJsonPath)
    {
        string jsonContent = File.ReadAllText(manifestJsonPath);
        return JsonSerializer.Deserialize<ModpackData>(jsonContent);
    }

    static string[] ReadModListLinks(string modListHtmlPath)
    {
        string htmlContent = File.ReadAllText(modListHtmlPath);
        var matches = Regex.Matches(htmlContent, @"<a\s+[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>");
        return matches.Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
    }

    static string SanitizeName(string name)
    {
        return Regex.Replace(name, "[^a-zA-Z0-9]", "_");
    }

    static void WriteModpackInfo(string outputFilePath, ModpackData modpackData)
    {
        using (StreamWriter writer = new StreamWriter(outputFilePath))
        {
            writer.WriteLine($"Modpack Name: {modpackData.name}");
            writer.WriteLine($"Modpack Version: {modpackData.version}");
            writer.WriteLine("Files:");
            foreach (var file in modpackData.files)
            {
                writer.WriteLine($"- Project ID: {file.projectID}, File ID: {file.fileID}, Link: {file.link}");
            }
        }
    }

    static async Task<string> DownloadModpack(string downloadLink) {
        var browserFetcherOptions = new BrowserFetcherOptions();
        await new BrowserFetcher(browserFetcherOptions).DownloadAsync();

        using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            DefaultViewport = null
        });

        using var page = await browser.NewPageAsync();
        await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/61.0.3163.100 Safari/537.36");
        await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
        await page.Client.SendAsync("Page.setDownloadBehavior", new
        {
            behavior = "allow",
            downloadPath = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
        });

        await page.Target.CreateCDPSessionAsync().Result.SendAsync("Page.setDownloadBehavior", new
        {
            behavior = "allow",
            downloadPath = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
        }, false);

        await page.GoToAsync(downloadLink);
        await page.WaitForTimeoutAsync(5000);

        string fileName = await page.EvaluateExpressionAsync<string>(
           "document.querySelector('.section-file-name p').innerText");

        string selector = ".btn-cta.download-cta";
        await page.WaitForSelectorAsync(selector);
        var downloadButtons = await page.QuerySelectorAllAsync(selector);

        if (downloadButtons.Count() > 1)
        {
            await downloadButtons[downloadButtons.Count() - 1].ClickAsync();
        }

        await page.WaitForTimeoutAsync(9000);

        Console.WriteLine($"Waiting for file: {fileName}");

        await WaitForFileAsync(AppDomain.CurrentDomain.BaseDirectory, fileName);

        Console.WriteLine($"Modpack downloaded!");

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
    }

    static async Task ProcessLinksAndDownloadAsync(List<FileData> files, string outputFolderPath)
    {
        var browserFetcherOptions = new BrowserFetcherOptions();
        await new BrowserFetcher(browserFetcherOptions).DownloadAsync();
        string modsFolder = Path.Combine(outputFolderPath, "mods");

        using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            DefaultViewport = null
        });



        if (!Directory.Exists(modsFolder))
        {
          Directory.CreateDirectory(modsFolder);
        }

        Console.WriteLine($"Mods to download: {files.Count}");
        Console.WriteLine($"Output folder: {modsFolder}");

        foreach (var file in files)
        {
            string downloadLink = $"{file.link}/files/{file.fileID}/";

            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/61.0.3163.100 Safari/537.36");
            await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
            await page.Client.SendAsync("Page.setDownloadBehavior", new
            {
                behavior = "allow",
                downloadPath = Path.GetFullPath(modsFolder)
            });

            await page.Target.CreateCDPSessionAsync().Result.SendAsync("Page.setDownloadBehavior", new
            {
                behavior = "allow",
                downloadPath = Path.GetFullPath(modsFolder)
            }, false);

            await page.GoToAsync(downloadLink);

            await page.WaitForTimeoutAsync(5000);

            string selector = ".btn-cta.download-cta";
            await page.WaitForSelectorAsync(selector);
            var downloadButtons = await page.QuerySelectorAllAsync(selector);

            if (downloadButtons.Count() > 1)
            {
                await downloadButtons[downloadButtons.Count() - 1].ClickAsync();
            }
            
            await page.WaitForTimeoutAsync(9000);
            Console.WriteLine($"{file.fileID} downloaded!");
        }
    }

    static async Task WaitForFileAsync(string directory, string fileName)
    {
        while (!File.Exists(Path.Combine(directory, fileName)))
        {
            Console.Write(".");
            await Task.Delay(1000);
        }
    }


    static bool IsWebUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri uriResult) && uriResult.Scheme == Uri.UriSchemeHttp;
    }
}


public class ModpackData
{
    public ModpackData()
    {
        version = "";
    }

    public class ModLoader
    {
        public string? id { get; set; }
        public bool primary { get; set; }
    }

    public dynamic minecraft { get; set; }
    public List<ModLoader> modLoaders { get; set; }
    public string? manifestType { get; set; }
    public int manifestVersion { get; set; }
    public string? name { get; set; }
    public string? version { get; set; }
    public string? author { get; set; }
    public int projectID { get; set; }
    public List<FileData> files { get; set; }
    public string? overriders { get; set; }
}
public class FileData
{
    public int projectID { get; set; }
    public int fileID { get; set; }
    public bool required { get; set; }
    public string? link { get; set; }
}
