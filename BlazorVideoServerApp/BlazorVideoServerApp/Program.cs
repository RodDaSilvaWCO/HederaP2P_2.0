using BlazorVideoServerApp.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Unosys.SDK;

namespace BlazorVideoServerApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            foreach( var arg in args )
            {
                Debug.Print(arg);
            }
            #region Validate and Parse incoming Content reference
            if (args == null || args.Length == 0)
            {
                return;
            }
            if( args.Length < 2 )
            {
                return;
            }
            if( ! int.TryParse( args[0], out var port ) ) { return; }
            var contentRef = args[1];
            
            if (string.IsNullOrEmpty(contentRef))
            {
                return;
            }
            var content = new ContentRef(contentRef);            
            //try
            //{
            //    Uri contentUri = new Uri(contentRef);
            //    Debug.Print(contentUri.ToString());
            //}
            //catch (Exception)
            //{
            //    return;
            //}
            #endregion



            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => { return true; };
            var builder = WebApplication.CreateBuilder(args);

            builder.WebHost.UseUrls($"https://localhost:{port}");

            // Add services to the container.
            builder.Services.AddRazorPages();
            builder.Services.AddServerSideBlazor();
            builder.Services.AddSingleton<WeatherForecastService>();
            builder.Services.AddSingleton(content);
            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStaticFiles();

            app.UseRouting();
            app.MapBlazorHub();

            app.MapControllers();

            //app.MapGet("/video", async context =>
            //{
            //    var filename = @"C:\lionking2019.mp4";  
            //    //Build the File Path.  
            //    //string path = Path.Combine(_hostenvironment.WebRootPath, "files/") + filename;  // the video file is in the wwwroot/files folder  

            //    var filestream = System.IO.File.OpenRead(filename);
            //    await Task.FromResult(Results.File(filestream, contentType: "video/mp4", fileDownloadName: filename, enableRangeProcessing: true));
            //});

            //app.MapGet("/image", async context =>
            //{
            //    var filename = @"C:\Rodney\Hockey.jpg";
            //    byte[] file = await System.IO.File.ReadAllBytesAsync(filename);

            //    var filestream = System.IO.File.OpenRead(filename);
            //    await Task.FromResult(Results.File(filestream, contentType: "image/jpeg")); //, fileDownloadName: filename, enableRangeProcessing: true));
            //});


           

            app.MapFallbackToPage("/_Host");



            app.Run();
        }
    }

    public class ContentRef
    {
        internal Guid VDiskID;
        internal Guid VolumeID;
        internal Guid UserSessionTokenID;
        //internal string ContentFile;
        //internal Unosys.SDK.FileStream ContentStream;
        public ContentRef(string contentRef)
        {
            VDiskID = new Guid(contentRef.Substring(0, 32));
            VolumeID = new Guid(contentRef.Substring(32, 32));
            UserSessionTokenID = new Guid(contentRef.Substring(64, 32));
            //ContentFile = Encoding.Unicode.GetString(ConvertHexStringToBytes(contentRef.Substring(96)));
            API.Initialize(VDiskID, VolumeID, "U" + UserSessionTokenID.ToString("N").ToUpper());
         
        }

       
    }
}