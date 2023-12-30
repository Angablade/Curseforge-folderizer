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

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Modpack Processing...");

        string zipFilePath = args[0];
        
        if(!File.Exists(zipFilePath) && zipFilePath.ToLower().StartsWith("http"))
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
                for (int i = 0; i < Math.Min(modLinks.Length, modpackData.Files.Count); i++)
                {
                    modpackData.Files[i].Link = modLinks[i];
                }
            }
        }catch (Exception ex)
        {
            Console.WriteLine(ex.HResult);
            Console.WriteLine("Failed to read Modlist.html, trying alt method.");
            List<Task> tasks = new List<Task>();
            foreach (var file in modpackData.Files)
            {
                tasks.Add(ProcessFileAsync(file));
            }
            await Task.WhenAll(tasks);
        }


        string outputFilePath = Path.Combine(outputFolderPath, $"{SanitizeName(modpackData.Name)}_{modpackData.Version}.txt");
        Console.WriteLine($"Writing modpack info to: {outputFilePath}");
        WriteModpackInfo(outputFilePath, modpackData);

        Console.WriteLine("Processing mod links and downloading files...");
        await ProcessLinksAndDownloadAsync(modpackData.Files, outputFolderPath);

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
                    if (entry.Length > 3) { 
                        Console.WriteLine("Reading file: " + entry);
                        string entryName = DetectAndConvertEntryName(zipArchive, entry.FullName);
                        entryName = entryName.Substring(entryName.IndexOf("/") + 1).Replace("/".ToArray()[0], Path.DirectorySeparatorChar);

                        try { 
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
                        }catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
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
    static async Task ProcessFileAsync(FileData file)
    {
        try
        {
            try {
                Console.WriteLine("Trying Google hook.");
                file.Link = await GetFirstGoogleSearchResultUrl(file.ProjectID.ToString());
            }catch (Exception e) { Console.WriteLine(e.ToString()); }
            
            if(file.Link.Length <= 3) {
                try
                {
                    Console.WriteLine("Trying Yahoo hook.");
                    file.Link = await GetFirstYahooSearchResultUrl(file.ProjectID.ToString());
                }
                catch (Exception e) { Console.WriteLine(e.ToString()); }
            }

            if (file.Link.StartsWith("#")) {
                file.Link = "https://www.curseforge.com/minecraft/texture-packs/" + file.Link.Replace("#", "");
            }
            else {
                file.Link = "https://www.curseforge.com/minecraft/mc-mods/" + file.Link;
            }
            Console.WriteLine($"Project Mask: {file.ProjectID.ToString()} -> {file.Link}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get link for project ID {file.ProjectID}. Exception: {ex}");
            Environment.Exit(1);
        }
    }
    static void WriteModpackInfo(string outputFilePath, ModpackData modpackData)
    {
        using (StreamWriter writer = new StreamWriter(outputFilePath))
        {
            writer.WriteLine($"Modpack Name: {modpackData.Name}");
            writer.WriteLine($"Modpack Version: {modpackData.Version}");
            writer.WriteLine("Files:");
            foreach (var file in modpackData.Files)
            {
                writer.WriteLine($"- Project ID: {file.ProjectID}, File ID: {file.FileID}, Link: {file.Link}");
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
        Console.WriteLine($"Output folder: {modsFolder}");

        foreach (var file in files)
        {
            string downloadLink = $"{file.Link}/files/{file.FileID}";

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
            Console.WriteLine($"{file.FileID} downloaded!");
        }
    }

    static async Task<string> WaitForFileAsync(string directory, string fileName)
    {
        while (true)
        {
            if (File.Exists(Path.Combine(directory, fileName))){
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

    static async Task<string> GetFirstGoogleSearchResultUrl(string query)
    {
        string firstResultUrl = "";
        using (HttpClient client = new HttpClient())
        {
            HttpResponseMessage response = await client.GetAsync($"https://www.google.com/search?q=curseforge+project+ID+\"{query}\"");
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
    static async Task<string> GetFirstYahooSearchResultUrl(string query)
    {
        string firstResultUrl = "";
        using (HttpClient client = new HttpClient())
        {
            HttpResponseMessage response = await client.GetAsync($"https://search.yahoo.com/search?p=curseforge+project+ID+\"{query}\"");
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
                Console.WriteLine("[HTTP] 503 Yahoo Hook Failed!");
            }
        }

        return "";
    }
}


public class ModpackData
{
    public ModpackData()
    {
        Version = "";
    }

    public class ModLoader
    {
        public string? Id { get; set; }
        public bool Primary { get; set; }
    }

    public dynamic? Minecraft { get; set; }
    public List<ModLoader>? ModLoaders { get; set; }
    public string? ManifestType { get; set; }
    public int ManifestVersion { get; set; }
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public int projectID { get; set; }
    public List<FileData>? Files { get; set; }
    public string? Overriders { get; set; }
}
public class FileData
{
    public int ProjectID { get; set; }
    public int FileID { get; set; }
    public bool Required { get; set; }
    public string? Link { get; set; }
}
