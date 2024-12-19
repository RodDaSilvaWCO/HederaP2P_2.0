using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Unosys.SDK;
using UnoSys.Api;

namespace BlazorVideoServerApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MediaController : Controller
    {
        Unosys.SDK.FileStream fileStream = null!;
        public MediaController(ContentRef contentRef) : base()
        {
            //fileStream = new Unosys.SDK.FileStream(contentRef.ContentFile, Unosys.SDK.FileMode.Open, Unosys.SDK.FileAccess.Read, Unosys.SDK.FileShare.Read);
            //fileStream = contentRef.ContentStream;
        }

        [HttpGet]
        public async Task<FileStreamResult> Index([FromQuery(Name = "contentRef")] string contentRef)
        {
            Debug.Print($"Inside Index() ");
            //var contentRef = "3800450041003100340042003600310045003200330045003400320043003600420045003200420032003200440030004200440043004100380041003300330020002000200020002000200020002000200020002000200030005F00300030005F00480065006C006C006F0020004600750074007500720065002000480065006400650072006100500032005000200031002E00300020004800610063006B006100740068006F006E00200050006900740063006800200056006900640065006F002E0041006C006900630065002E0063006E007400";
            var contentFile = Encoding.Unicode.GetString(ConvertHexStringToBytes(contentRef));
            var fileStream = new Unosys.SDK.FileStream(contentFile, Unosys.SDK.FileMode.Open, Unosys.SDK.FileAccess.Read, Unosys.SDK.FileShare.Read);
            return await Task.FromResult( new FileStreamResult(fileStream, "video/mp4"));
            //Stream filestream = null!;
            //try
            //{
            //API.Initialize(new Guid("88487a3b-8a44-4f46-8f03-ab171db3d11f"), new Guid("88487a3b-8a44-4f46-8f03-ab171db3d11f"), "UEBB2B4AAEDF2B627463EC99CE196EBFB");
            //await Task.CompletedTask;
            //var filename = @"C:\lionking2019.mp4";
            //Stream filestream = System.IO.File.OpenRead(filename);
            //filestream = new Unosys.SDK.FileStream(@"lionking2019.mp4", Unosys.SDK.FileMode.Open, Unosys.SDK.FileAccess.Read,
            // Unosys.SDK.FileShare.Read);
            //}
            //catch(Exception ex)
            //{
            //
            //}
            //return new FileStreamResult(filestream, "video/mp4");



        }

        private static byte[] ConvertHexStringToBytes(string hexString)
        {
            // Convert Hex string to byte[]
            byte[] HexAsBytes = new byte[hexString.Length / 2];
            for (int index = 0; index < HexAsBytes.Length; index++)
            {
                string byteValue = hexString.Substring(index * 2, 2);
                HexAsBytes[index] = byte.Parse(byteValue, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            }
            return HexAsBytes;
        }

        //// GET: MediaController
        //public ActionResult Index()
        //{
        //    return View();
        //}

        //// GET: MediaController/Details/5
        //public ActionResult Details(int id)
        //{
        //    return View();
        //}

        //// GET: MediaController/Create
        //public ActionResult Create()
        //{
        //    return View();
        //}

        //// POST: MediaController/Create
        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public ActionResult Create(IFormCollection collection)
        //{
        //    try
        //    {
        //        return RedirectToAction(nameof(Index));
        //    }
        //    catch
        //    {
        //        return View();
        //    }
        //}

        //// GET: MediaController/Edit/5
        //public ActionResult Edit(int id)
        //{
        //    return View();
        //}

        //// POST: MediaController/Edit/5
        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public ActionResult Edit(int id, IFormCollection collection)
        //{
        //    try
        //    {
        //        return RedirectToAction(nameof(Index));
        //    }
        //    catch
        //    {
        //        return View();
        //    }
        //}

        //// GET: MediaController/Delete/5
        //public ActionResult Delete(int id)
        //{
        //    return View();
        //}

        //// POST: MediaController/Delete/5
        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public ActionResult Delete(int id, IFormCollection collection)
        //{
        //    try
        //    {
        //        return RedirectToAction(nameof(Index));
        //    }
        //    catch
        //    {
        //        return View();
        //    }
        //}
    }
}
