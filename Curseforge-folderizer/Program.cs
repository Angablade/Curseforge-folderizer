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
using System.Web;
using System.Collections;
using System.Diagnostics;

class Program
{
    static async Task Main(string[] args)
    {
       
        if (args.Length == 0 )
        {
            KillSpecificChromeProcess();
            Environment.Exit(0);
        }


        Console.WriteLine("Starting Modpack Processing...");
        string zipFilePath = args[0];

        if (!File.Exists(zipFilePath) && zipFilePath.ToLower().StartsWith("http"))
        {
            Console.WriteLine("Downloading modpack!");
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

        try
        {
            Console.WriteLine("Reading modlist.html");
            string modlistHtmlPath = Path.Combine(extractionDirectory, "modlist.html");
            string[] modLinks = ReadModListLinks(modlistHtmlPath);

            if (modLinks.Length > 0)
            {
                for (int i = 0; i < Math.Min(modLinks.Length, modpackData.files.Count); i++)
                {
                    modpackData.files[i].link = modLinks[i];
                    modpackData.files[i].mask = modLinks[i].TrimEnd("/".ToCharArray()[0]);
                    modpackData.files[i].mask = modpackData.files[i].mask.Substring(modpackData.files[i].mask.LastIndexOf("/"));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to read Modlist.html, trying alt method.");
            List<Task> tasks = new List<Task>();
            foreach (var file in modpackData.files)
            {
                tasks.Add(ProcessFileAsync(file));
            }
            await Task.WhenAll(tasks);
        }


        string outputFilePath = Path.Combine(outputFolderPath, $"{SanitizeName(modpackData.name)}_{modpackData.version}.txt");
        Console.WriteLine($"Writing modpack info to: {outputFilePath}");
        WriteModpackInfo(outputFilePath, modpackData);

        Console.WriteLine("Processing mod links and downloading files...");
        await ProcessLinksAndDownloadAsync(modpackData.files, outputFolderPath);

        Console.WriteLine($"Deleting extraction directory: {extractionDirectory}");
        Directory.Delete(extractionDirectory, true);
        Console.WriteLine("Processing completed successfully.");
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
                    if (entry.Length > 3)
                    {
                        Console.WriteLine("Reading file: " + entry);
                        string entryName = DetectAndConvertEntryName(zipArchive, entry.FullName);
                        entryName = entryName.Substring(entryName.IndexOf("/") + 1).Replace("/".ToArray()[0], Path.DirectorySeparatorChar);

                        try
                        {
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
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                        }

                    }

                });

            }
        }
        catch (Exception ex)
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
    static async Task ProcessFileAsync(FileData file)
    {
        try
        {
            List<Func<string, Task<string>>> searchEngines = new List<Func<string, Task<string>>>
            {
                GetFirstGoogleSearchResultUrl,
                GetFirstYahooSearchResultUrl,
                GetFirstDuckDuckGoSearchResultUrl
            };
            foreach (var searchEngine in searchEngines)
            {
                try
                {
                    Console.WriteLine($"Trying {searchEngine.Method.Name} hook.".Replace("GetFirstGoogleSearchResultUrl","GoogleAPI").Replace("GetFirstYahooSearchResultUrl", "YahooAPI").Replace("GetFirstDuckDuckGoSearchResultUrl", "DuckDuckGoAPI"));
                    file.link = await searchEngine(file.projectID.ToString());
                    if (file.link.Length > 3) {
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"{searchEngine.Method.Name} hook Failed!");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }

            if (file.link.StartsWith("#"))
            {
                file.mask = file.link.Replace("-", " ");
                file.link = "https://www.curseforge.com/minecraft/texture-packs/" + file.link.Replace("#", "");
            }
            else
            {
                file.mask = file.link.Replace("-"," ");
                file.link = "https://www.curseforge.com/minecraft/mc-mods/" + file.link;
            }
            Console.WriteLine($"Project Mask: {file.projectID.ToString()} -> {file.mask}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get link for project ID {file.projectID}. Exception: {ex}");
            Environment.Exit(1);
        }
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

    static async Task<string> DownloadModpack(string downloadLink)
    {
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

        fileName = await WaitForFileAsync(AppDomain.CurrentDomain.BaseDirectory, fileName);
        Console.WriteLine($"Found: {fileName}!");

        Console.WriteLine($"Modpack downloaded!");

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
    }

    static async Task ProcessLinksAndDownloadAsync(List<FileData> files, string outputFolderPath)
    {
        var browserFetcherOptions = new BrowserFetcherOptions();
        await new BrowserFetcher(browserFetcherOptions).DownloadAsync();
        string modsFolder = Path.Combine(outputFolderPath, "mods");
        string textFolder = Path.Combine(outputFolderPath, "texturepacks");

        using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            DefaultViewport = null
        });

        if (!Directory.Exists(modsFolder)) { Directory.CreateDirectory(modsFolder); }
        if (!Directory.Exists(textFolder)) { Directory.CreateDirectory(textFolder); }

        Console.WriteLine($"Mods to download: {files.Count}");
        Console.WriteLine("[File ID] [Status]    [Mask Name]     [Progress]");
        //Console.WriteLine($"Output folder: {modsFolder}");

        foreach (var file in files)
        {
            string downloadLink = $"{file.link}/files/{file.fileID}";

            Console.Write($"[{file.fileID:D7}] Downloading {Truncate(file.mask, 15),-15} ");


            using var page = await browser.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/61.0.3163.100 Safari/537.36");
            await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
            if (downloadLink.Contains("/texture-packs/"))
            {
                await page.Client.SendAsync("Page.setDownloadBehavior", new
                {
                    behavior = "allow",
                    downloadPath = Path.GetFullPath(textFolder)
                });

                await page.Target.CreateCDPSessionAsync().Result.SendAsync("Page.setDownloadBehavior", new
                {
                    behavior = "allow",
                    downloadPath = Path.GetFullPath(textFolder)
                }, false);
            }
            else
            {
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
            }

            Console.Write(".");
            await page.GoToAsync(downloadLink);
            Console.Write(".");
            await page.WaitForTimeoutAsync(5000);
            Console.Write(".");
            string selector = ".btn-cta.download-cta";
            await page.WaitForSelectorAsync(selector);
            var downloadButtons = await page.QuerySelectorAllAsync(selector);
            Console.Write(".");
            if (downloadButtons.Count() > 1)
            {
                await downloadButtons[downloadButtons.Count() - 1].ClickAsync();
            }
            Console.Write(".");
            await page.WaitForTimeoutAsync(7000);
            Console.WriteLine(" [ok]");
        }
    }

    static async Task<string> WaitForFileAsync(string directory, string fileName)
    {
        while (true)
        {
            if (File.Exists(Path.Combine(directory, fileName)))
            {
                Console.WriteLine($"Finding: {fileName}");
                break;
            }

            if (File.Exists(Path.Combine(directory, fileName.Replace(" ", "+"))))
            {
                fileName = fileName.Replace(" ", "+");
                Console.WriteLine($"Finding: {fileName}");
                break;
            }
            Console.Write(".");
            await Task.Delay(2000);
        }
        return fileName;
    }

    static async Task<string> GetFirstSearchResultUrl(string searchEngineUrl, string query)
    {
        string firstResultUrl = "";
        using (HttpClient client = new HttpClient())
        {
            HttpResponseMessage response = await client.GetAsync($"{searchEngineUrl}\"{query}\"");
            Thread.Sleep(5000);
            if (response.IsSuccessStatusCode)
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                foreach (string str in responseContent.Split('"'))
                {
                    string decodedStr = HttpUtility.UrlDecode(str).Replace("/url?q=", "");
                    if (decodedStr.Contains("curseforge.com"))
                    {
                        if (decodedStr.Contains("/mc-mods/"))
                        {
                            try
                            {
                                string substring = decodedStr.Substring(decodedStr.IndexOf("/mc-mods/"));
                                substring = substring.Replace("/mc-mods/", "");
                                if (substring.Contains("/")) { substring = substring.Substring(0, substring.IndexOf("/")); }
                                if (substring.Contains("&")) { substring = substring.Substring(0, substring.IndexOf("&")); }
                                firstResultUrl = substring;
                                return firstResultUrl;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.ToString());
                            }
                        }
                        if (decodedStr.Contains("/projects/"))
                        {
                            try
                            {
                                string substring = decodedStr.Substring(decodedStr.IndexOf("/projects/"));
                                substring = substring.Replace("/projects/", "");
                                if (substring.Contains("/")) { substring = substring.Substring(0, substring.IndexOf("/")); }
                                if (substring.Contains("&")) { substring = substring.Substring(0, substring.IndexOf("&")); }
                                firstResultUrl = substring;
                                return firstResultUrl;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.ToString());
                            }
                        }
                        if (decodedStr.Contains("/texture-packs/"))
                        {
                            try
                            {
                                string substring = decodedStr.Substring(decodedStr.IndexOf("/texture-packs/"));
                                substring = substring.Replace("/texture-packs/", "");
                                if (substring.Contains("/")) { substring = substring.Substring(0, substring.IndexOf("/")); }
                                if (substring.Contains("&")) { substring = substring.Substring(0, substring.IndexOf("&")); }
                                firstResultUrl = "#" + substring;
                                return firstResultUrl;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.ToString());
                            }
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("[HTTP] 503 Google Hook Failed!");
            }
        }

        return "";
    }

    static async Task<string> GetFirstGoogleSearchResultUrl(string query)
    {
        return await GetFirstSearchResultUrl("https://www.google.com/search?q=site%3Acurseforge.com+Project+ID%3A+", query);
    }
    static async Task<string> GetFirstYahooSearchResultUrl(string query)
    {
        return await GetFirstSearchResultUrl("https://search.yahoo.com/search?p=site%253Acurseforge.com+Project+ID%253A+", query);
    }
    static async Task<string> GetFirstDuckDuckGoSearchResultUrl(string query)
    {
        return await GetFirstSearchResultUrl("https://duckduckgo.com/?t=h_&q=site%253Acurseforge.com+Project+ID%253A+", query);
    }

    public static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    static async Task KillSpecificChromeProcess()
    {
        var options = new LaunchOptions
        {
            Headless = true
        };

        using (var browser = await Puppeteer.LaunchAsync(options))
        {
            var page = await browser.NewPageAsync();
            await page.CloseAsync();
        }
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
    public string? mask { get; set; }
    public string? link { get; set; }
}
