using DocumentTextExtractorApi.Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DocumentTextExtractorApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DocumentParserController : ControllerBase
    {
        private readonly TimeSpan maxWaitForProcess = TimeSpan.FromSeconds(300);
        private readonly ILogger _logger;

        private SemaphoreQueue commandSemaphore = new SemaphoreQueue(1, 1);

        public DocumentParserController(ILogger<DocumentParserController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public string Home()
        {
            return "Please send raw file with a POST request to this endpoint.\n";
        }

        // POST api/<DocumentParserController>
        [HttpPost]
        [RequestSizeLimit(1000000000)]
        public async Task<IActionResult> Post()
        {
            var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms);
            ms.Seek(0, SeekOrigin.Begin);

            try
            {
                await commandSemaphore.WaitAsync();

                _logger.LogInformation($"{DateTime.Now}: Received request, attempting to parse it as LaTeX file...");
                var result = await GetTextFromLaTeX(ms);
                if (result != null)
                {
                    _logger.LogInformation($"{DateTime.Now}: LaTeX parse successful. Result length = {result.Length}");
                    return Content(result, "text/plain", Encoding.UTF8);
                }

                _logger.LogInformation($"{DateTime.Now}: attempting to parse it as zipped LaTeX group file containing 'main.tex'...");
                result = await GetTextFromLaTeXZip(ms);
                if (result != null)
                {
                    _logger.LogInformation($"{DateTime.Now}: LaTeX zip group parse successful. Result length = {result.Length}");
                    return Content(result, "text/plain", Encoding.UTF8);
                }

                //result = await GetTextFromDocxUsingCustomLibrary(ms);
                //if (result != null)
                //    return Content(result);

                //result = await GetTextFromDoc(ms);
                //if (result != null)
                //    return Content(result);

                _logger.LogInformation($"{DateTime.Now}: attempting to parse it as a word document...");
                result = await GetTextFromDocDocxUsingLibreOffice(ms);
                if (result != null)
                {
                    _logger.LogInformation($"{DateTime.Now}: LibreOffice parse successful. Result length = {result.Length}");
                    return Content(result, "text/plain", Encoding.UTF8);
                }

                _logger.LogInformation($"{DateTime.Now}: Failed to parse the input file. Will respond with 415.");
                return StatusCode(415);
            }
            finally
            {
                commandSemaphore.Release();
            }
        }

        private async Task<string> GetTextFromDoc(MemoryStream ms)
        {
            var tmpPath = Path.GetTempPath();
            var tmpDocFile = Path.GetRandomFileName().Replace(".", "");

            try
            {
                using (FileStream file = new FileStream(Path.Combine(tmpPath, tmpDocFile), FileMode.Create, FileAccess.Write))
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    await ms.CopyToAsync(file);
                    file.Close();
                }

                RunCommand($"cd \"{tmpPath}\" && lowriter --convert-to docx {tmpDocFile}");
                
                using (FileStream docx = new FileStream(Path.Combine(tmpPath, tmpDocFile + ".docx"), FileMode.Open, FileAccess.Read))
                {
                    return GetTextFromDocxUsingCustomLibrary(docx);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"GetTextFromDoc failed: {ex}");
                return null;
            }
            finally
            {
                if (System.IO.File.Exists(tmpDocFile + ".docx"))
                    System.IO.File.Delete(tmpDocFile + ".docx");
                if (System.IO.File.Exists(tmpDocFile))
                    System.IO.File.Delete(tmpDocFile);
            }
        }

        private async Task<string> GetTextFromDocDocxUsingLibreOffice(MemoryStream ms)
        {
            var tmpPath = Path.GetTempPath();
            var tmpDocFile = Path.GetRandomFileName().Replace(".", "");

            try
            {
                using (FileStream file = new FileStream(Path.Combine(tmpPath, tmpDocFile), FileMode.Create, FileAccess.Write))
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    await ms.CopyToAsync(file);
                    file.Close();
                }

                RunCommand($"cd \"{tmpPath}\" && lowriter --convert-to 'txt:Text (encoded):UTF8' {tmpDocFile}");
                var data = await System.IO.File.ReadAllBytesAsync(Path.Combine(tmpPath, tmpDocFile + ".txt"));
                var text = Encoding.UTF8.GetString(data);
                return text;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"GetTextFromDocDocxUsingLibreOffice failed: {ex}");
                return null;
            }
            finally
            {
                if (System.IO.File.Exists(tmpDocFile + ".txt"))
                    System.IO.File.Delete(tmpDocFile + ".txt");
                if (System.IO.File.Exists(tmpDocFile))
                    System.IO.File.Delete(tmpDocFile);
            }
        }

        private async Task<string> GetTextFromLaTeXZip(MemoryStream ms)
        {           
            var tmpPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName().Replace(".", ""));
            Directory.CreateDirectory(tmpPath);

            try
            {
                try
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    var zipFile = new ZipArchive(ms);
                    ZipFileExtensions.ExtractToDirectory(zipFile, tmpPath, true);
                }
                catch (Exception)
                {
                    _logger.LogInformation("Received file is not a zip file. GetTextFromLaTeXZip will return null.");
                    return null;
                }

                var tmpFile = Path.Combine(tmpPath, "main.tex");
                if (!System.IO.File.Exists(tmpFile))
                {
                    _logger.LogInformation("main.tex not found in the received zip file. GetTextFromLaTeXZip will return null.");
                    return null;
                }

                RunCommand($"cd \"{tmpPath}\" && untex -m -o -e -a -i {tmpFile} > {tmpFile}.txt");
                var textData = await System.IO.File.ReadAllBytesAsync(tmpFile + ".txt");
                var text = Encoding.UTF8.GetString(textData);
                return text;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"GetTextFromLaTeXZip failed: {ex}");
                return null;
            }
            finally
            {
                if (Directory.Exists(tmpPath))
                    Directory.Delete(tmpPath, true);
            }
        }

        private async Task<string> GetTextFromLaTeX(MemoryStream ms)
        {
            ms.Seek(0, SeekOrigin.Begin);
            var data = ms.ToArray();
            if (data.Contains((byte)0))
            {
                _logger.LogInformation("File is binary, so not a latex file.");
                return null; // This is a binary file, so definitely not a latex file.
            }

            var tmpPath = Path.GetTempPath();
            var tmpFile = Path.GetRandomFileName().Replace(".", "");

            try
            {
                using (FileStream file = new FileStream(Path.Combine(tmpPath, tmpFile + ".tex"), FileMode.Create, FileAccess.Write))
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    await ms.CopyToAsync(file);
                    file.Close();
                }

                RunCommand($"cd \"{tmpPath}\" && untex -m -o -e -a {tmpFile}.tex > {tmpFile}.txt");
                var textData = await System.IO.File.ReadAllBytesAsync(Path.Combine(tmpPath, tmpFile + ".txt"));
                var text = Encoding.UTF8.GetString(textData);
                return text;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"GetTextFromLaTeX failed: {ex}");
                return null;
            }
            finally
            {
                if (System.IO.File.Exists(tmpFile + ".tex"))
                    System.IO.File.Delete(tmpFile + ".tex");
                if (System.IO.File.Exists(tmpFile + ".txt"))
                    System.IO.File.Delete(tmpFile + ".txt");
            }
        }

        private string GetTextFromDocxUsingCustomLibrary(Stream ms)
        {
            try
            {
                var docxToText = new DocxToText(ms);
                var text = docxToText.ExtractText();

                if (text.Contains('\0'))
                    return null;

                return text;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"GetTextFromDocxUsingCustomLibrary failed: {ex}");
                return null;
            }
        }

        private bool RunCommand(string command)
        {
            using (System.Diagnostics.Process proc = new System.Diagnostics.Process())
            {
                proc.StartInfo.FileName = "/bin/bash";
                proc.StartInfo.Arguments = "-c \" " + command + " \"";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.Start();

                _logger.LogInformation($"{DateTime.Now}: Running command '{command}'...");

                var exited = proc.WaitForExit((int)maxWaitForProcess.TotalMilliseconds);
                if (!exited)
                {
                    _logger.LogInformation($"{DateTime.Now}: Command did not exit after {maxWaitForProcess.TotalSeconds} seconds. Will try to kill it.");
                    try
                    {
                        proc.Kill();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"{DateTime.Now}: Killing command failed. Will ignore. Exception: {ex}");
                    }
                    return false;
                }
            }
            return true;
        }
    }
}
