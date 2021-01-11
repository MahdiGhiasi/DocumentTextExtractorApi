using DocumentTextExtractorApi.Classes;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
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

            var result = await GetTextFromLaTeX(ms);
            if (result != null)
                return Content(HttpUtility.HtmlEncode(result));

            //result = await GetTextFromDocxUsingCustomLibrary(ms);
            //if (result != null)
            //    return Content(result);

            //result = await GetTextFromDoc(ms);
            //if (result != null)
            //    return Content(result);

            result = await GetTextFromDocDocxUsingLibreOffice(ms);
            if (result != null)
                return Content(HttpUtility.HtmlEncode(result));

            return StatusCode(415);
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

                var result = RunCommand($"cd \"{tmpPath}\" && lowriter --convert-to docx {tmpDocFile}");
                
                using (FileStream docx = new FileStream(Path.Combine(tmpPath, tmpDocFile + ".docx"), FileMode.Open, FileAccess.Read))
                {
                    return GetTextFromDocxUsingCustomLibrary(docx);
                }
            }
            catch (Exception ex)
            {
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

                var result = RunCommand($"cd \"{tmpPath}\" && lowriter --convert-to 'txt:Text (encoded):UTF8' {tmpDocFile}");
                var data = await System.IO.File.ReadAllBytesAsync(Path.Combine(tmpPath, tmpDocFile + ".txt"));
                var text = Encoding.UTF8.GetString(data);
                return text;
            }
            catch (Exception ex)
            {
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

        private async Task<string> GetTextFromLaTeX(MemoryStream ms)
        {
            ms.Seek(0, SeekOrigin.Begin);
            var data = ms.ToArray();
            if (data.Contains((byte)0))
                return null; // This is a binary file, so definitely not a latex file.

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

                var result = RunCommand($"cd \"{tmpPath}\" && detex {tmpFile}.tex");
                return result;
            }
            catch (Exception ex)
            {
                return null;
            }
            finally
            {
                if (System.IO.File.Exists(tmpFile + ".tex"))
                    System.IO.File.Delete(tmpFile + ".tex");
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
            catch
            {
                return null;
            }
        }

        private string RunCommand(string command)
        {
            string result = "";
            using (System.Diagnostics.Process proc = new System.Diagnostics.Process())
            {
                proc.StartInfo.FileName = "/bin/bash";
                proc.StartInfo.Arguments = "-c \" " + command + " \"";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.Start();

                result += proc.StandardOutput.ReadToEnd();
                result += proc.StandardError.ReadToEnd();

                proc.WaitForExit();
            }
            return result;
        }
    }
}
