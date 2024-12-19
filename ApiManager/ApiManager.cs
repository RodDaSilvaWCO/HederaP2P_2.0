namespace UnoSysKernel
{
    #region Usings
    using System;
    using System.Threading;
	using System.Threading.Tasks;
	using UnoSysCore;
	using UnoSys.Api.Models;
    using UnoSys.Api.Interfaces;
    using System.Diagnostics;
    using System.Text.Json;
    using System.Collections.Generic;
    using System.Net;
    using System.IO;
	using System.Runtime.CompilerServices;
	using Microsoft.Extensions.Configuration;
	using Microsoft.Extensions.DependencyInjection;
	using Microsoft.AspNetCore.Builder;
	using System.Security.Authentication;
	using Microsoft.AspNetCore.Hosting;
	using Microsoft.Extensions.Hosting;
	using System.Security.Cryptography.X509Certificates;
	using Microsoft.AspNetCore.Server.Kestrel.Core;
	using Microsoft.AspNetCore.Mvc;
	using System.Security.Cryptography;
	using UnoSys.Api;
	using UnoSys.Api.Exceptions;
    using Microsoft.Extensions.Logging;
    using System.Transactions;

    //using Microsoft.Extensions.Logging;
    #endregion

    internal sealed partial class ApiManager : SecuredKernelService, IApiManager 
	{
		#region Member Fields
		private IApiContext apiContext = null!;
		private Task runInBackground = null!;
		private IHost? host;
        private ILocalNodeContext localNodeContext = null!;
		private IWorldComputerContext wcContext = null!;
		private ISessionManager sessionManager = null!;
		private IFileManager fileManager = null!;
        private ISpawnManager spawnManager = null!;
        private IStatisticsManager statisticsManager = null!;
        private ITime timeManager = null!;
		private IPublishedContentManager publishedContentManager = null!;
		private ISecurityContext securityContext = null!;
        private IVirtualDiskManager virtualDiskManager = null!;
        private IGeneralLedgerManager generalLedgerManager = null!;
        private IIdentityManager identityManager = null!;
        private Task ioTask = Task.CompletedTask;
        private int defaultLocalListeningPort = -1;
        private int nodeControllerPort = -1;
        private Delegate writeToConsole = null!;
        private string nodeDirectoryPath = null!;
        #endregion

        #region Constructors
        public ApiManager(  IGlobalPropertiesContext globalProps,
                            ILoggerFactory loggerFactory, 
							IKernelConcurrencyManager concurrencyManager, 
                            ILocalNodeContext localnodecontext,
							IWorldComputerContext wccontext, 
							ISecurityContext securitycontext,
							IApiContext apicontext, 
							ISessionManager sesssionmanager, 
							IFileManager filemanager, 
                            ISpawnManager spawnmanager,
							IPublishedContentManager pcmanager, 
                            IStatisticsManager statsmanager,
                            ITime timemanager,
                            IVirtualDiskManager virtualdiskmanager,
                            IGeneralLedgerManager generalledgermanager,
                            IIdentityManager identitymanager
                          ) : base(loggerFactory.CreateLogger("ApiManager"), concurrencyManager)
        {
            defaultLocalListeningPort = globalProps.DefaultLocalListeningPort;
            writeToConsole = globalProps.WriteToConsole!;
            apiContext = apicontext;
            localNodeContext = localnodecontext;
			wcContext = wccontext;
			securityContext = securitycontext;
			sessionManager = sesssionmanager;
			fileManager = filemanager;
            spawnManager = spawnmanager;
            timeManager = timemanager;
            statisticsManager = statsmanager;   
            publishedContentManager = pcmanager;
            virtualDiskManager = virtualdiskmanager;
            generalLedgerManager = generalledgermanager;
            identityManager = identitymanager;
            nodeControllerPort = globalProps.KernelOptions!.NodeControllerPort;
            nodeDirectoryPath = globalProps.NodeDirectoryPath;

            // Create a secured object for this kernel service 
            securedObject = securityContext.CreateDefaultKernelServiceSecuredObject();
        }
        #endregion

        #region IDisposable Implementation
        public override void Dispose()
        {
            apiContext = null!;
            localNodeContext = null!;
            wcContext = null!;
            securityContext = null!;
            sessionManager = null!;
            fileManager = null!;
            spawnManager = null!;
            statisticsManager = null!;
            timeManager = null!;
            publishedContentManager = null!;
            writeToConsole = null!;
            virtualDiskManager = null!;
            nodeDirectoryPath = null!;
            identityManager = null!;
            runInBackground = null!;
            host?.Dispose();
            host = null!;
            generalLedgerManager = null!;

            //tableStateRegistry?.Dispose();	
            //tableStateRegistry = null!;
            //tableStateConcurrencylock = null!;
            base.Dispose();
        }
        #endregion 

        #region IKernelService Implementation
        [MethodImpl(MethodImplOptions.NoInlining)]
		public override async Task StartAsync(CancellationToken cancellationToken)
		{
			using (var exclusiveLock = await _concurrencyManager.KernelLock.WriterLockAsync().ConfigureAwait(false))
			{
				//runInBackground = apiContext.ApiStart(Marshal.GetFunctionPointerForDelegate(apiImplementationCallback));
				//runInBackground = UnoSysKernel.FirmwareApi.Start(Marshal.GetFunctionPointerForDelegate(apiImplementationCallback));
				//runInBackground = apiHostService.StartAsync(CancellationToken.None);
			runInBackground = StartAsync2(cancellationToken);
				await base.StartAsync(cancellationToken).ConfigureAwait(false);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public override async Task StopAsync(CancellationToken cancellationToken)
		{
			using (var exclusiveLock = await _concurrencyManager.KernelLock.WriterLockAsync().ConfigureAwait(false))
			{
				//apiContext.ApiStop().GetAwaiter().GetResult();
				//UnoSysKernel.FirmwareApi.Stop().GetAwaiter().GetResult();
				//apiHostService.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
			 StopAsync2(cancellationToken).GetAwaiter().GetResult();
			runInBackground.GetAwaiter().GetResult();
				await base.StopAsync(cancellationToken).ConfigureAwait(false);
			}
		}


		[MethodImpl(MethodImplOptions.NoInlining)]
        public async Task StartAsync2(CancellationToken cancellationToken)
        {
            try
            {
                //var url = string.Format(kernelOptions.ShellBaseUrlTemplate, 
                //    EntryPoint.ComputeBasePort(kernelOptions.NodeControllerPort) + 2);
                string url = null!;
                if (defaultLocalListeningPort == -1)
                {
                    url = $"https://localhost:{nodeControllerPort}";
                }
                else
                {
                    url = $"https://localhost:{defaultLocalListeningPort}";
                }
                Console.WriteLine($"url={url}");
                //var withOriginsTemplate = $"{string.Format(kernelOptions.ShellBaseUrlTemplate,EntryPoint.ComputeBasePort(kernelOptions.NodeControllerPort))}, {string.Format(kernelOptions.ShellBaseUrlTemplate, EntryPoint.ComputeBasePort(kernelOptions.NodeControllerPort) + 1)}";
                //var apiUrl = string.Format(kernelOptions.InternalStsBaseUrlTemplate,
				//						  EntryPoint.ComputeBasePort(kernelOptions.NodeControllerPort) + 3);  // URL of API
                //PAL.Log($"UnoSysKernel.ApiHostService - API Listening on url={url}");
				var encryptedOrgUnit = HostCryptology.AsymmetricEncryptionWithoutCertificate(
											localNodeContext.NodeID.ToByteArray(), localNodeContext.Node2048AsymmetricPublicKey);
                var dynpwd = HostCryptology.Random16BytesAsGuid().ToString("N");
                var hostBuilder = CreateHostBuilder(url,
                                         new X509Certificate2(
                                         //CertificateManager.GenerateLocalHostSigningCert("localhost", "WCNode", "WCO", 10, dynpwd), dynpwd),
                                         CertificateManager.GenerateLocalHostSigningCert("localhost", "World Computer Organization",
                                                HostCryptology.ConvertBytesToHexString(encryptedOrgUnit), 10, dynpwd), dynpwd)); //,
                                         //withOriginsTemplate,
										 //apiUrl);

				host = hostBuilder.Build();
                if (host != null!)
                {
                    if (!GlobalPropertiesContext.IsSimulatedNode())
                    {
                        // writeToConsole.DynamicInvoke($"{GlobalPropertiesContext.ThisNodeNumber()}","");
                    //    //Console.Title = $"{GlobalPropertiesContext.ThisNodeNumber()}";
                    //}
                    //else
                    //{
                        writeToConsole.DynamicInvoke("WCNode", $"World Computer Node has booted and is listening on {url}...", ConsoleColor.Black);
                    }
                    await host.RunAsync(cancellationToken);
				}
            }
            catch(Exception ex)
            {
                PAL.Log($"Error Inside ApiManager.StartAsync2({ex.ToString()})");
            }
        }

		[MethodImpl(MethodImplOptions.NoInlining)]
        public async Task StopAsync2(CancellationToken cancellationToken)
        {
            if (host != null!)
            {
                try
                {
					await host.StopAsync(cancellationToken);
                }
                catch(Exception ex)
                {
                    PAL.Log($"Error Inside ApiHostedService.StopAsync({ex.ToString()})");
                }
            }
        }

		[MethodImpl(MethodImplOptions.NoInlining)]
		private IHostBuilder CreateHostBuilder(
										string urls,
										X509Certificate2 certificate//,
									//	string withOriginsTemplate,
									//	string apiUrl
            ) =>
				Host.CreateDefaultBuilder(null)
					.ConfigureWebHostDefaults(webBuilder => {
                    webBuilder.UseKestrel(options =>
                    {
                        options.ConfigureEndpointDefaults(opts =>
                        {
                            opts.Protocols = HttpProtocols.Http1;  // Required for <= Win8.1 support
                        });
                        options.ConfigureHttpsDefaults(httpsOptions =>
                        {
                            httpsOptions.SslProtocols = SslProtocols.None;
                            httpsOptions.ServerCertificate = certificate;
                        });
                    })
                    .UseUrls(urls)
                    .ConfigureLogging(builder => builder.ClearProviders())
                    //.ConfigureLogging(builder => builder.AddDebug())
                    //.ConfigureLogging(builder => builder.AddConsole())
                    //  .ConfigureLogging(builder => builder.AddEventLog())
                    //.UseStartup<ApiStartup>();
                    .UseStartup<ApiStartup>((e) => new ApiStartup(localNodeContext, wcContext, this)); //, withOriginsTemplate, apiUrl));
                    });


	

        #endregion


        public async Task<ApiResult> ApiRouteToImplementationAsync(int apiid, int verb, int requestFormat, string jsonRequest )
        {
            var watch = Stopwatch.StartNew();
            int resultcode = 0;         // default for success
			string resultmessage = "";
            #region Flash node if in Animated Simulation Mode
            //var runInBackGround = EntryPoint.FlashNode(EntryPoint.FlashConsoleColorAPI);
            #endregion 
            ApiResponse response = new ApiResponse(((int)HttpStatusCode.NotFound));  // Default
			try
			{
                switch ((ApiIdentifier)apiid)
                {
                    #region Miscelaneous Apis
                    case ApiIdentifier.Ping:
                        {
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;

                    case ApiIdentifier.TestRead:
                        {
                            try
                            {
                                List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                                validParams.Add(new ApiParameterValidation("filePath", typeof(string)));
                                var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                                var filePath = args.GetValue<string>("filePath");
                                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                                {
                                    byte[] bytes = new byte[3];
                                    fs.Read(bytes, 0, 3);
                                    Debug.Print($"TESTREAD: '{filePath}' =  byte1={bytes[0]}, byte2={bytes[1]}, byte3={bytes[2]}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.Print($"Error in ApiManager(TESTREAD) - {ex.ToString()}");
                            }
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;

                    case ApiIdentifier.TestWrite:
                        {
                            try
                            {
                                List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                                validParams.Add(new ApiParameterValidation("filePath", typeof(string)));
                                var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                                var filePath = args.GetValue<string>("filePath");
                                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                                {
                                    byte[] bytes = new byte[3];
                                    bytes[0] = 65;
                                    bytes[1] = 66;
                                    bytes[2] = 67;
                                    fs.Write(bytes, 0, 3);
                                    fs.Flush();
                                    fs.Close();
                                    Debug.Print($"TESTWRITE: '{filePath}' Writing {3} bytes to 'M:\\Rod.txt'");
                                }
                                //                        {
                                //using (var fs = new FileStream(@"M:\rod.txt", FileMode.Open, FileAccess.Read, FileShare.None))
                                //{
                                //	byte[] bytes = new byte[3];
                                //	fs.Read(bytes, 0, 3);
                                //	Debug.Print($"byte1={bytes[0]}, byte2={bytes[1]}, byte3={bytes[2]}");
                                //}
                            }
                            catch (Exception ex)
                            {
                                Debug.Print($"Error in ApiManager(TESTWRITE) - {ex.ToString()}");
                            }
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;


                    case ApiIdentifier.TestDelete:
                        {
                            try
                            {
                                List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                                validParams.Add(new ApiParameterValidation("filePath", typeof(string)));
                                var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                                var filePath = args.GetValue<string>("filePath");
                                File.Delete(filePath);
                                if (File.Exists(filePath))
                                {

                                    Debug.Print($"TESTDELETE: '{filePath}' failed deleted");
                                }
                                else
                                {
                                    Debug.Print($"TESTDELETE: '{filePath}' succeeded deleted");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.Print($"Error in ApiManager(TESTDELETE) - {ex.ToString()}");
                            }
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;

                    case ApiIdentifier.TestCreateDir:
                        {
                            try
                            {
                                List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                                validParams.Add(new ApiParameterValidation("filePath", typeof(string)));
                                var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                                var filePath = args.GetValue<string>("filePath");
                                var dir = Directory.CreateDirectory(filePath);
                                if (dir.Exists)
                                {
                                    Debug.Print($"TESTCREATEDIR: Succeeded for  '{filePath}' ");
                                }
                                else
                                {
                                    Debug.Print($"TESTCREATEDIR: FAILED for  '{filePath}' ");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.Print($"Error in ApiManager(TESTCREATEDIR) - {ex.ToString()}");
                            }
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;


                    case ApiIdentifier.TestDeleteDir:
                        {
                            try
                            {
                                List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                                validParams.Add(new ApiParameterValidation("filePath", typeof(string)));
                                var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                                var filePath = args.GetValue<string>("filePath");
                                Directory.Delete(filePath);
                                if (Directory.Exists(filePath))
                                {
                                    Debug.Print($"TESTDELETEDIR: FAILED for  '{filePath}' ");
                                }
                                else
                                {
                                    Debug.Print($"TESTDELETEDIR: SUCCEEDED for  '{filePath}' ");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.Print($"Error in ApiManager(TESTDELETEDIR) - {ex.ToString()}");
                            }
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;

                    case ApiIdentifier.ShutDownNode:
                        {
                            // This operation takes no Parameters  
                            // and returns "OK""
                            //
                            //var runInBackGround = Firmware.DelayedShutDown(1);
                            response = ApiResponse.CreateApiResponseWithResult<string>(await ShutDownNodeAsync().ConfigureAwait(false));
                        }
                        break;

                    case ApiIdentifier.SimulationVisualization:
                        {
                            // This operation takes no Parameters  
                            // and returns "OK""
                            //
                            response = ApiResponse.CreateApiResponseWithResult<string>(await SimulationVisualizationAsync().ConfigureAwait(false));
                        }
                        break;

                    #endregion

                    #region UnoSys OS Apis
                    case ApiIdentifier.UnoSysUserSessionsCount:
                        {
                            // This operation takes no parameters and returns the number of current User Sessions that have been established on this node
                            //
                            // Declare required arguments
                            response = ApiResponse.CreateApiResponseWithResult<int>(await UnoSysUserSessionsCountAsync().ConfigureAwait(false));
                        }
                        break;

                    case ApiIdentifier.UnoSysApplicationSessionsCount:
                        {
                            // This operation takes no parameters and returns the number of current Application Sessions that have been established on this node
                            //
                            // Declare required arguments
                            response = ApiResponse.CreateApiResponseWithResult<int>(await UnoSysApplicationSessionsCountAsync().ConfigureAwait(false));
                        }
                        break;

                    case ApiIdentifier.UnoSysDatabaseSessionsCount:
                        {
                            // This operation takes no parameters and returns the number of current Database Sessions that have been established on this node
                            //
                            // Declare required arguments
                            response = ApiResponse.CreateApiResponseWithResult<int>(await UnoSysDatabaseSessionsCountAsync().ConfigureAwait(false));
                        }
                        break;

                    case ApiIdentifier.UnoSysTableSessionsCount:
                        {
                            // This operation takes no parameters and returns the number of current Table Sessions that have been established on this node
                            //
                            // Declare required arguments
                            response = ApiResponse.CreateApiResponseWithResult<int>(await UnoSysTableSessionsCountAsync().ConfigureAwait(false));
                        }
                        break;

                    case ApiIdentifier.UnoSysDNABlobCount:
                        {
                            // This operation takes no parameters and returns the number of in-memory blobs that are being stored by this node
                            //
                            // Declare required arguments
                            response = ApiResponse.CreateApiResponseWithResult<ulong>(await UnoSysDNABlobCountAsync().ConfigureAwait(false));
                        }
                        break;

                    case ApiIdentifier.UnoSysDNABlockCount:
                        {
                            // This operation takes no parameters and returns the number of in-memory blocks that are being stored by this node
                            //
                            // Declare required arguments
                            response = ApiResponse.CreateApiResponseWithResult<ulong>(await UnoSysDNABlockCountAsync().ConfigureAwait(false));
                        }
                        break;

                    #endregion 

                    #region Application Apis
                    case ApiIdentifier.ApplicationConnect:
						{
							// This operation takes a UserSessionToken, as well as Application's unique AppId and Shared Secret AppKey 
							// and returns an opaque AppSessionToken
							//
							// Declare required arguments
							List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("ApplicationId", typeof(string)));
							validParams.Add(new ApiParameterValidation("AppKey", typeof(string)));
							// Retrieve the received validated arguments from the request 
							var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var appId = args.GetValue<string>("ApplicationId");
							var appKey = args.GetValue<string>("AppKey");
                            response = ApiResponse.CreateApiResponseWithResult<string>(await ApplicationConnectAsync(userSessionToken, appId, appKey).ConfigureAwait(false));
						}
						break;


					case ApiIdentifier.ApplicationDisconnect:
						{
							// This operation takes an opaque AppSessionToken and returns nothing
							//
							// Declare required arguments
							List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("SessionToken", typeof(string)));
							// Retrieve the received validated arguments from the request 
							var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var sessionToken = args.GetValue<string>("SessionToken");
                            ApplicationDisconnect(userSessionToken, sessionToken);
                            response = new ApiResponse();  // Empty 'OK' success response
						}
						break;


                    case ApiIdentifier.ApplicationReadDatabaseNames:
                        {
                            // This operation takes a UserSessionToken, an opaque Application SessionToken and returns a List of Database References
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("SessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var appSessionToken = args.GetValue<string>("SessionToken");
                            response = ApiResponse.CreateApiResponseWithResult<string[]>(
                                    await ApplicationReadDatabaseNamesAsync(userSessionToken, appSessionToken).ConfigureAwait(false));
                        }
                        break;


                    case ApiIdentifier.ApplicationCreateDatabase:
                        {
                            // This operation takes aUserSessionToken, an opaque Application SessionToken, a Database Name and a Database Description,
                            // and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("ApplicationSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DatabaseName", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DatabaseDescription", typeof(string), isRequired: false, isNullable: true));  // optional, nullable
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var appSessionToken = args.GetValue<string>("ApplicationSessionToken");
                            var databaseName = args.GetValue<string>("DatabaseName");
                            var databaseDescription = args.GetValue<string>("DatabaseDescription");
                            await ApplicationCreateDatabaseAsync(userSessionToken, appSessionToken, databaseName, databaseDescription).ConfigureAwait(false);
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;

                    case ApiIdentifier.ApplicationDeleteDatabase:
                        {
                            // This operation takes aUserSessionToken, an opaque Application SessionToken and a Database Name,
                            // and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("ApplicationSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DatabaseName", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var appSessionToken = args.GetValue<string>("ApplicationSessionToken");
                            var databaseName = args.GetValue<string>("DatabaseName");
                            await ApplicationDeleteDatabaseAsync(userSessionToken, appSessionToken, databaseName).ConfigureAwait(false);
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;
                    #endregion


                    #region Database Apis
                    case ApiIdentifier.DatabaseConnect:
                        {
                            // This operation takes a UserSessionToken and an opaque SessionToken, DatabaseName, DesiredAccess, ShareMode and SecurityDescriptor
                            // and returns an opaque DatabaseSessionToken
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("SessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DatabaseName", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DesiredAccess", typeof(int)));
                            validParams.Add(new ApiParameterValidation("ShareMode", typeof(int)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var sessionToken = args.GetValue<string>("SessionToken");
                            var databaseName = args.GetValue<string>("DatabaseName");
                            var desiredAccess = args.GetValue<int>("DesiredAccess");
                            var shareMode = args.GetValue<int>("ShareMode");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("SessionToken", sessionToken);
                            //ThrowIfParameterNullOrEmpty("DatabaseName", databaseName);
                            //if (desiredAccess == 0)
                            //{
                            //    throw new UnoSysArgumentException("Parameter 'DesiredAccess' is uninitialized.");
                            //}
                            //if (shareMode == 0)
                            //{
                            //    throw new UnoSysArgumentException("Parameter 'ShareMode' is uninitialized.");
                            //}
                            response = ApiResponse.CreateApiResponseWithResult<string>(await DatabaseConnectAsync(userSessionToken,
                                        sessionToken, databaseName, (DatabaseAccessType)desiredAccess, (DatabaseShareType)shareMode).ConfigureAwait(false));
                        }
                        break;

                    case ApiIdentifier.DatabaseDisconnect:
                        {
                            // This operation takes a UserSessionToken and an opaque DatabaseSessionToken and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DatabaseSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var databaseSessionToken = args.GetValue<string>("DatabaseSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("DatabaseSessionToken", databaseSessionToken);
                            DatabaseDisconnect(userSessionToken, databaseSessionToken);
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;

                    case ApiIdentifier.DatabaseCreateTable:
                        {
                            // This operation takes a UserSessionToken and an opaque DatabaseSessionToken, Table Name, Table Description, Table Schema, DesiredAccess, and ShareMode
                            // and returns a TableOperationResponse
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DatabaseSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableName", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableDescription", typeof(string), isRequired: false, isNullable:true ));  // optional, nullable
                            validParams.Add(new ApiParameterValidation("TableSchema", typeof(TableSchema)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var databaseHandle = args.GetValue<string>("DatabaseSessionToken");
                            var tableName = args.GetValue<string>("TableName");
                            var tableDescription = args.GetValue<string>("TableDescription");
                            var tableSchema = args.GetValue<TableSchema>("TableSchema");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("DatabaseSessionToken", databaseHandle);
                            //ThrowIfParameterNullOrEmpty("TableName", tableName);
                            //if (tableSchema == null!)
                            //{
                            //    throw new UnoSysArgumentException("Parameter 'TableSchema' must not be null.");
                            //}
                            await DatabaseCreateTableAsync(userSessionToken, databaseHandle, tableName, tableDescription, tableSchema ).ConfigureAwait(false);
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;


                    case ApiIdentifier.DatabaseDeleteTable:
                        {
                            // This operation takes a UserSessionToken and an opaque DatabaseSessionToken and a Table Name and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DatabaseSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableName", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var databaseSessionToken = args.GetValue<string>("DatabaseSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableName");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("DatabaseSessionToken", databaseHandle);
                            //ThrowIfParameterNullOrEmpty("TableName", tableReference);
                            DatabaseDeleteTable(userSessionToken, databaseSessionToken, tableSessionToken);
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;


                    case ApiIdentifier.DatabaseReadTableNames:
                        {
                            // This operation takes an opaque DatabaseSessionToken and returns a List of Table Names
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DatabaseSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var databaseSessionToken = args.GetValue<string>("DatabaseSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("DatabaseSessionToken", databaseHandle);
                            response = ApiResponse.CreateApiResponseWithResult<string[]>(await DatabaseReadTableNamesAsync(userSessionToken, databaseSessionToken).ConfigureAwait(false));
                        }
                        break;


                    #endregion


                    #region Table Apis
                    case ApiIdentifier.TableOpen:
                        {
                            // This operation takes an opaque DatabaseSessionToken, Table Reference, DesiredAccess, and ShareMode
                            // and returns an opaque TableSessionToken
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DatabaseSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableName", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DesiredAccess", typeof(int)));
                            validParams.Add(new ApiParameterValidation("ShareMode", typeof(int)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var dbSessionToken = args.GetValue<string>("DatabaseSessionToken");
                            var tableName = args.GetValue<string>("TableName");
                            var desiredAccess = args.GetValue<int>("DesiredAccess");
                            var shareMode = args.GetValue<int>("ShareMode");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("DatabaseSessionToken", dbSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableName", tableName);
                            //if (desiredAccess == 0)
                            //{
                            //    throw new UnoSysArgumentException("Parameter 'DesiredAccess' is uninitialized.");
                            //}
                            //if (shareMode == 0)
                            //{
                            //    throw new UnoSysArgumentException("Parameter 'ShareMode' is uninitialized.");
                            //}
                            response = ApiResponse.CreateApiResponseWithResult<string>(
                                        await TableOpenAsync(userSessionToken, dbSessionToken, tableName,(DatabaseAccessType)desiredAccess, (DatabaseShareType)shareMode).ConfigureAwait(false));
                        }
                        break;


                    case ApiIdentifier.TableClose:
                        {
                            // This operation takes an opaque TableSessionToken and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
                            TableClose(userSessionToken, tableSessionToken);
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;


                    case ApiIdentifier.TableGoFirst:
                        {
                            // This operation takes an opaque TableSessionToken and returns a ulong representing the current Logical Record
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
						    await TableGoFirstAsync(userSessionToken, tableSessionToken).ConfigureAwait(false);
							response = SetApiResponseFromTableSessionToken(tableSessionToken);
                        }
                        break;


                    case ApiIdentifier.TableGoLast:
                        {
                            // This operation takes an opaque TableSessionToken and returns a ulong representing the current Logical Record
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
							await TableGoLastAsync(userSessionToken, tableSessionToken).ConfigureAwait(false);
                            response = SetApiResponseFromTableSessionToken(tableSessionToken);
                        }
                        break;


                    case ApiIdentifier.TableGoTo:
                        {
                            // This operation takes an opaque TableSessionToken and Logical Record to goto,
							// and returns a ulong representing the current Logical Record
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("LogicalRecordOrdinal", typeof(ulong)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            var logicalRecordOrdinal = args.GetValue<ulong>("LogicalRecordOrdinal");
							await TableGoToAsync(userSessionToken, tableSessionToken, logicalRecordOrdinal).ConfigureAwait(false);
                            response = SetApiResponseFromTableSessionToken(tableSessionToken);
                        }
                        break;


                    case ApiIdentifier.TableSkip:
                        {
                            // This operation takes an opaque TableSessionToken and a number of recordds to skip,
							// and returns a ulong representing the current Logical Record
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("RecordCount", typeof(int)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            var recordCount = args.GetValue<int>("RecordCount");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
							await TableSkipAsync(userSessionToken, tableSessionToken, recordCount).ConfigureAwait(false);
                            response = SetApiResponseFromTableSessionToken(tableSessionToken);
                        }
                        break;


                    case ApiIdentifier.TableSeek:
                        {
                            // This operation takes an opaque TableSessionToken and an Order Key,
							// and returns a ulong representing the current Logical Record
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("OrderKey", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            var orderKey = args.GetValue<string>("Orderkey");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
							await TableSeekAsync(userSessionToken, tableSessionToken, orderKey).ConfigureAwait(false);
                            response = SetApiResponseFromTableSessionToken(tableSessionToken);
                        }
                        break;


                    case ApiIdentifier.TableIsBoT:
                        {
                            // This operation takes an opaque TableSessionToken and returns a bool
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
							await TableIsBoTAsync(userSessionToken, tableSessionToken).ConfigureAwait(false);
                            response = SetApiResponseFromTableSessionToken(tableSessionToken);
                        }
                        break;


                    case ApiIdentifier.TableIsEoT:
                        {
                            // This operation takes an opaque TableSessionToken and returns a bool
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
                            await TableIsEoTAsync(userSessionToken, tableSessionToken).ConfigureAwait(false);
                            response = SetApiResponseFromTableSessionToken(tableSessionToken);
                        }
                        break;


                    case ApiIdentifier.TableIsDirty:
                        {
                            // This operation takes an opaque TableSessionToken and returns a bool
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
                            await TableIsDirtyAsync(userSessionToken, tableSessionToken).ConfigureAwait(false);
                            response = SetApiResponseFromTableSessionToken(tableSessionToken);
                        }
                        break;


                    case ApiIdentifier.TableAppendRecord:
                        {
                            // This operation takes an opaque TableSessionToken 
                            // and returns a ulong representing the current Logical Record
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
                            await TableAppendRecordAsync(userSessionToken, tableSessionToken).ConfigureAwait(false);
                            response = SetApiResponseFromTableSessionToken(tableSessionToken);
                        }
                        break;


                    case ApiIdentifier.TableDeleteRecord:
                        {
                            // This operation takes an opaque tableSessionToken and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
                            try
                            {
                                TableDeleteRecord(userSessionToken, tableSessionToken);
                                response = new ApiResponse();  // Empty 'OK' success response
                            }
                            catch
                            {
                                // Catch case where the value passed in as an AppSessionToken is not validly encrypted (i.e.; wrong encryption padding, etc.;)
                                response = new ApiResponse(((int)HttpStatusCode.NotFound));
                            }
                        }
                        break;


                    case ApiIdentifier.TableRecallRecord:
                        {
                            // This operation takes an opaque tableSessionToken and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
                            try
                            {
                                TableRecallRecord(userSessionToken, tableSessionToken);
                                response = new ApiResponse();  // Empty 'OK' success response
                            }
                            catch
                            {
                                // Catch case where the value passed in as an AppSessionToken is not validly encrypted (i.e.; wrong encryption padding, etc.;)
                                response = new ApiResponse(((int)HttpStatusCode.NotFound));
                            }
                        }
                        break;



                    case ApiIdentifier.TableRecordCount:
                        {
                            // This operation takes an opaque TableSessionToken 
                            // and returns a ulong representing the number of logical records
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
                            await TableRecordCountAsync(userSessionToken, tableSessionToken).ConfigureAwait(false);
                            response = SetApiResponseFromTableSessionToken(tableSessionToken);
                        }
                        break;


                    case ApiIdentifier.TableRecordOrdinal:
                        {
                            // This operation takes an opaque TableSessionToken 
                            // and returns a ulong representing the current Logical Record
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
                            await TableRecordOrdinalAsync(userSessionToken, tableSessionToken).ConfigureAwait(false);
                            response = SetApiResponseFromTableSessionToken(tableSessionToken);
                        }
                        break;


                    case ApiIdentifier.TableCommitRecord:
                        {
                            // This operation takes an opaque TableSessionToken 
                            // and returns a ulong representing the current Logical Record
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
                            await TableCommitRecordAsync(userSessionToken, tableSessionToken).ConfigureAwait(false);
                            response = SetApiResponseFromTableSessionToken(tableSessionToken);
                        }
                        break;


                    case ApiIdentifier.TableRollbackRecord:
                        {
                            // This operation takes an opaque tableSessionToken and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
                            try
                            {
                                TableRollbackRecord(userSessionToken, tableSessionToken);
                                response = new ApiResponse();  // Empty 'OK' success response
                            }
                            catch
                            {
                                // Catch case where the value passed in as an AppSessionToken is not validly encrypted (i.e.; wrong encryption padding, etc.;)
                                response = new ApiResponse(((int)HttpStatusCode.NotFound));
                            }
                        }
                        break;


                    case ApiIdentifier.TableSchema:
                        {
                            // This operation takes an opaque TableSessionToken 
                            // and returns a TableSchema object
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
							response = ApiResponse.CreateApiResponseWithResult<TableSchema>(
							await TableSchemaAsync(userSessionToken, tableSessionToken).ConfigureAwait(false));
                        }
                        break;


                    case ApiIdentifier.TableFieldGet:
                        {
                            // This operation takes an opaque tableSessionToken, and a FieldOrdinal and returns a Value as an object )
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            var fieldOrdinal = args.GetValue<int>("FieldOrdinal");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
                            response = ApiResponse.CreateApiResponseWithResult<object>(
                                            await TableFieldGetAsync(userSessionToken, tableSessionToken, fieldOrdinal).ConfigureAwait(false));
                        }
						break;



                    case ApiIdentifier.TableFieldPut:
                        {
                            // This operation takes an opaque tableSessionToken, a FieldOrdinal and a Value (as a string) and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("TableSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var tableSessionToken = args.GetValue<string>("TableSessionToken");
                            var fieldOrdinal = args.GetValue<int>("FieldOrdinal");
                            var fieldValue = args.GetValue<string>("FieldValue");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("TableSessionToken", tableSessionToken);
                            try
                            {
                                TableFieldPut( userSessionToken, tableSessionToken, fieldOrdinal, fieldValue);
                                response = new ApiResponse();  // Empty 'OK' success response
                            }
                            catch
                            {
                                // Catch case where the value passed in as an AppSessionToken is not validly encrypted (i.e.; wrong encryption padding, etc.;)
                                response = new ApiResponse(((int)HttpStatusCode.NotFound));
                            }
                        }
                        break;
                    #endregion


                    #region Volume Apis
     //               case ApiIdentifier.VolumeMount:
					//	{
					//		// Declare required arguments
					//		List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
					//		validParams.Add(new ApiParameterValidation("SessionToken", typeof(string)));
					//		// Retrieve the received validated arguments from the request 
					//		var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
					//		var sessionToken = args.GetValue<string>("SessionToken");
     //                       ThrowIfParameterNullOrEmpty("SessionToken", sessionToken);
					//		var volumeId = fileManager.MountSsidVolumeAsync(sessionToken).Result;
					//		// Create an ApiResponse with a string Result
					//		response = ApiResponse.CreateApiResponseWithResult<ulong>(volumeId); 
					//	}
					//	break;


					//case ApiIdentifier.VolumeUnmount:
					//	{
					//		// Declare required arguments
					//		List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
					//		validParams.Add(new ApiParameterValidation("VolumeId", typeof(ulong) ));
					//		// Retrieve the received validated arguments from the request 
					//		var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
					//		if( fileManager.UnmountSsidVolumeAsync(args.GetValue<ulong>("VolumeId") ).Result ) 
     //                       {
					//			response = new ApiResponse();  // OK
					//		}
					//		else
     //                       {
					//			response = new ApiResponse((int)HttpStatusCode.NotFound);
     //                       }
					//	}
					//	break;
                    #endregion


                    #region User Apis
                    case ApiIdentifier.UserLogin:
						{
                            // This operation takes the User's UserName and Shared Secret "Password" 
                            // and returns an opaque "subject" UserSessionToken
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            //validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("UserName", typeof(string)));
							validParams.Add(new ApiParameterValidation("Password", typeof(string)));
							var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            //var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var userName = args.GetValue<string>("UserName");
                            var password = args.GetValue<string>("Password");
                            try
							{
                                response = ApiResponse.CreateApiResponseWithResult<string>(
											await UserLoginAsync(/*userSessionToken,*/ userName, password ).ConfigureAwait(false));
                            }
                            catch(UnoSysArgumentException  )
                            {
                                throw ;
                            }
							catch(UnoSysResourceNotFoundException )
                            {
                                throw ;
                            }
                            catch 
                            {

                                throw new UnoSysUnauthorizedAccessException();
                            }
						}
						break;


					case ApiIdentifier.UserLogout:
						{
                            // This operation takes an opaque SubjectUserSessionToken and returnns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
							//validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string) ));
                            validParams.Add(new ApiParameterValidation("SubjectUserSessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
							//var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var subjectUserSessionToken = args.GetValue<string>("SubjectUserSessionToken");
							UserLogout(/*userSessionToken,*/ subjectUserSessionToken);
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
						break;


                    case ApiIdentifier.UserReadDatabaseNames:
                        {
                            // This operation takes an opaque User SessionToken and returns a List of Database References
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("SessionToken", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("SessionToken");
                            //ThrowIfParameterNullOrEmpty("SessionToken", userSessionToken);
                            response = ApiResponse.CreateApiResponseWithResult<string[]>(await UserReadDatabaseNamesAsync(userSessionToken).ConfigureAwait(false));
                        }
                        break;


                    case ApiIdentifier.UserCreateDatabase:
                        {
                            // This operation takes a "context" UserSessionToken, a "subject" UserSessionToken, a Database Name and a Database Description,
                            // and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("SubjectUserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DatabaseName", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DatabaseDescription", typeof(string), isRequired: false, isNullable: true));  // optional, nullable
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var subjectUserSessionToken = args.GetValue<string>("SubjectUserSessionToken");
                            var databaseName = args.GetValue<string>("DatabaseName");
                            var databaseDescription = args.GetValue<string>("DatabaseDescription");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("SubjectUserSessionToken", subjectUserSessionToken);
                            //ThrowIfParameterNullOrEmpty("DatabaseName", databaseName);
                            await UserCreateDatabaseAsync(userSessionToken, subjectUserSessionToken, databaseName, databaseDescription).ConfigureAwait(false);
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;

                    case ApiIdentifier.UserDeleteDatabase:
                        {
                            // This operation takes a "context" UserSessionToken, a "subject" UserSessionToken and a Database Name,
                            // and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("SubjectUserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DatabaseName", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var subjectUserSessionToken = args.GetValue<string>("SubjectUserSessionToken");
                            var databaseName = args.GetValue<string>("DatabaseName");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("SubjectUserSessionToken", subjectUserSessionToken);
                            //ThrowIfParameterNullOrEmpty("DatabaseName", databaseName);
                            await UserDeleteDatabaseAsync(userSessionToken, subjectUserSessionToken, databaseName).ConfigureAwait(false);
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;

                    case ApiIdentifier.UserCreate:
                        {
                            // This operation takes a "context" UserSessionToken, a UserName and a Password,
                            // and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("UserName", typeof(string)));
                            validParams.Add(new ApiParameterValidation("Password", typeof(string))); 
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var userName = args.GetValue<string>("UserName");
                            var password = args.GetValue<string>("Password");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("UserName", userName);
                            //ThrowIfParameterNullOrEmpty("Password", password);
                            await UserCreateAsync(userSessionToken, userName, password).ConfigureAwait(false);
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;

                    case ApiIdentifier.UserDelete:
                        {
                            // This operation takes a "context" UserSessionToken, and a UserId,
                            // and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("UserName", typeof(string)));
                            validParams.Add(new ApiParameterValidation("Password", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var userName = args.GetValue<string>("UserName");
                            var password = args.GetValue<string>("Password");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("UserId", userId);
                            await UserDeleteAsync(userSessionToken, userName, password).ConfigureAwait(false);
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;


                    case ApiIdentifier.UserCreateApplication:
                        {
                            // This operation takes a "context" UserSessionToken, a SubjectUserSessionToken, an ApplicationName, an optional ApplicationDescription and an ApplicationKey,
                            // and returns the generated ApplicationId
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("SubjectUserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("ApplicationName", typeof(string)));
                            validParams.Add(new ApiParameterValidation("ApplicationDescription", typeof(string), isRequired: false, isNullable: true));  // optional, nullable
                            validParams.Add(new ApiParameterValidation("ApplicationKey", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var subjectUserSessionToken = args.GetValue<string>("SubjectUserSessionToken");
                            var appName = args.GetValue<string>("ApplicationName");
                            var appDescription = args.GetValue<string>("ApplicationDescription");
                            var appKey = args.GetValue<string>("ApplicationKey");
                            response = ApiResponse.CreateApiResponseWithResult<string>(await UserCreateApplicationAsync(userSessionToken, subjectUserSessionToken, 
                                                                    appName, appDescription, appKey).ConfigureAwait(false));
                        }
                        break;

                    case ApiIdentifier.UserDeleteApplication:
                        {
                            // This operation takes a "context" UserSessionToken, a SubjectUserSessionToken, and an ApplicationId
                            // and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("SubjectUserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("ApplicationId", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var subjectUserSessionToken = args.GetValue<string>("SubjectUserSessionToken");
                            var appId = args.GetValue<string>("ApplicationId");
                            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("SubjectUserSessionToken", userSessionToken);
                            //ThrowIfParameterNullOrEmpty("ApplicationId", appId);
                            await UserDeleteApplicationAsync(userSessionToken, subjectUserSessionToken, appId).ConfigureAwait(false);
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;

                    case ApiIdentifier.UserChangePassword:
                        {
                            // This operation takes a UserSessionToken, a User's UserName, the Old Password and the New Password 
                            // and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("UserName", typeof(string)));
                            validParams.Add(new ApiParameterValidation("OldPassword", typeof(string)));
                            validParams.Add(new ApiParameterValidation("NewPassword", typeof(string)));
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var userName = args.GetValue<string>("UserName");
                            var oldPassword = args.GetValue<string>("OldPassword");
                            var newPassword = args.GetValue<string>("NewPassword");
                            try
                            {
                                await UserChangePasswordAsync(userSessionToken, userName, oldPassword, newPassword).ConfigureAwait(false);
                            }
                            catch (UnoSysArgumentException)
                            {
                                throw;
                            }
                            catch (UnoSysResourceNotFoundException)
                            {
                                throw;
                            }
                            catch
                            {

                                throw new UnoSysUnauthorizedAccessException();
                            }
                            response = new ApiResponse();  // Empty 'OK' success response
                        }
                        break;
                    //case ApiIdentifier.GetPrimaryUserOSSIDFromUserSSID:
                    //                   {
                    //		// Declare required arguments
                    //		List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                    //		validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                    //		// Retrieve the received validated arguments from the request 
                    //		var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                    //		var userSessionToken = args.GetValue<string>("UserSessionToken");
                    //		if (userSessionToken == "")
                    //		{
                    //			throw new UnoSysArgumentException("Parameter 'UserSessionToken' must not be empty.");
                    //		}
                    //		var userSession = sessionManager.ResolveSsidSession(userSessionToken);
                    //		if (userSession != null!)
                    //		{
                    //			#region Process the Request
                    //			var ossid = overlayManager.GetPrimaryUserOSSIDFromUserSSID(userSession).Result;
                    //			if (!string.IsNullOrEmpty(ossid))
                    //			{
                    //				response = ApiResponse.CreateApiResponseWithResult<string>(ossid);
                    //			}
                    //			else
                    //			{
                    //				throw new FileNotFoundException();
                    //			}
                    //			#endregion
                    //		}
                    //		else
                    //		{
                    //			throw new UnauthorizedAccessException();
                    //		}
                    //	}
                    //	break;


                    //case ApiIdentifier.GetSSIDInfo:
                    //                   {
                    //                       // Declare required arguments
                    //                       List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                    //                       validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                    //                       // Retrieve the received validated arguments from the request 
                    //                       var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                    //                       var userSessionToken = args.GetValue<string>("UserSessionToken");
                    //                       if (userSessionToken == "")
                    //                       {
                    //                           throw new UnoSysArgumentException("Parameter 'UserSessionToken' must not be empty.");
                    //                       }
                    //                       var userSession = sessionManager.ResolveSsidSession(userSessionToken);
                    //                       if (userSession != null!)
                    //                       {

                    //			#region Process the Request
                    //			var ssidInfo = overlayManager.GetSSIDInfoAsync(userSession).Result;
                    //			string ssidName = null!;
                    //			string ssidDisplayName = null!;
                    //			if (!string.IsNullOrEmpty(ssidInfo))
                    //			{
                    //				string[] ssidInfoParts = ssidInfo.Split(new char[] { '|' });
                    //				ssidName = Encoding.Unicode.GetString(HostCryptology.ConvertHexStringToBytes(ssidInfoParts[1]));
                    //				ssidDisplayName = Encoding.Unicode.GetString(HostCryptology.ConvertHexStringToBytes(ssidInfoParts[2]));
                    //				// Declare required return values
                    //				List<ApiParameter> outputs = new List<ApiParameter>();
                    //				outputs.Add(new ApiParameter("SSIDName", typeof(string), ssidName));
                    //				outputs.Add(new ApiParameter("DefaultDisplayName", typeof(string), ssidDisplayName));
                    //				outputs.Add(new ApiParameter("SSID", typeof(long), ssidInfoParts[0]));
                    //				response = new ApiResponse(outputs);
                    //			}
                    //			else
                    //                           {
                    //				throw new FileNotFoundException();
                    //                           }
                    //                           #endregion
                    //                       }
                    //                       else
                    //                       {
                    //                           throw new UnauthorizedAccessException();
                    //                       }
                    //                   }
                    //                   break;
                    #endregion


                    #region General Ledger Apis
                    case ApiIdentifier.GeneralLedgerGetReport:
                        {
                            // This operation takes a "context" UserSessionToken, a "subject" SessionToken, a ReportType,
                            // a ReportOutputType, a FromUtcDate and a ToUtcDate 
                            // and returns a string
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("SubjectSessionToken", typeof(string), isRequired: false, isNullable: true));  // optional, nullable
                            validParams.Add(new ApiParameterValidation("ReportType", typeof(int)));
                            validParams.Add(new ApiParameterValidation("ReportOptions", typeof(int)));
                            validParams.Add(new ApiParameterValidation("FromUtcDate", typeof(string), isRequired: false, isNullable: true));  // optional, nullable
                            validParams.Add(new ApiParameterValidation("ToUtcDate", typeof(string), isRequired: false, isNullable: true));  // optional, nullable
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var subjectSessionToken = args.GetValue<string>("SubjectSessionToken");
                            var reportType = args.GetValue<int>("ReportType");
                            var reportOptions = args.GetValue<int>("ReportOptions");
                            var fromUtcDate = args.GetValue<string>("FromUtcDate");
                            var toUtcDate = args.GetValue<string>("ToUtcDate");
                            response = ApiResponse.CreateApiResponseWithResult<string>(
                                            await GeneralLedgerGetReportAsync(userSessionToken, subjectSessionToken, reportType, 
                                                        reportOptions, fromUtcDate, toUtcDate).ConfigureAwait(false));
                        }
                        break;

                    case ApiIdentifier.GeneralLedgerFundWallet:
                        {
                            // This operation takes a "context" UserSessionToken, a "subject" SessionToken, a DLT Address,
                            // a DLT Private Key, and a positive integer Funds Amount value 
                            // and returns a string
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("SubjectSessionToken", typeof(string), isRequired: false, isNullable: true));  // optional, nullable
                            validParams.Add(new ApiParameterValidation("DLTAddress", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DLTPrivateKey", typeof(string)));
                            validParams.Add(new ApiParameterValidation("FundsAmount", typeof(ulong)));
                            validParams.Add(new ApiParameterValidation("UnitOfAmount", typeof(int)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var subjectSessionToken = args.GetValue<string>("SubjectSessionToken");
                            var dltAddress = args.GetValue<string>("DLTAddress");
                            var dltPrivateKey = args.GetValue<string>("DLTPrivateKey");
                            var fundsAmount = args.GetValue<ulong>("FundsAmount");
                            var unitOfAmount = args.GetValue<int>("UnitOfAmount");
                            response = ApiResponse.CreateApiResponseWithResult<string>(
                                            await GeneralLedgerFundWalletAsync(userSessionToken, subjectSessionToken, 
                                                            dltAddress, dltPrivateKey, fundsAmount, unitOfAmount).ConfigureAwait(false));
                        }
                        break;

                    case ApiIdentifier.GeneralLedgerDefundWallet:
                        {
                            // This operation takes a "context" UserSessionToken, a "subject" SessionToken, a DLT Address,
                            // a DLT Private Key, and a positive integer Funds Amount value 
                            // and returns a string
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("SubjectSessionToken", typeof(string), isRequired: false, isNullable: true));  // optional, nullable
                            validParams.Add(new ApiParameterValidation("DLTAddress", typeof(string)));
                            validParams.Add(new ApiParameterValidation("DLTPrivateKey", typeof(string)));
                            validParams.Add(new ApiParameterValidation("FundsAmount", typeof(ulong)));
                            validParams.Add(new ApiParameterValidation("UnitOfAmount", typeof(int)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var subjectSessionToken = args.GetValue<string>("SubjectSessionToken");
                            var dltAddress = args.GetValue<string>("DLTAddress");
                            var dltPrivateKey = args.GetValue<string>("DLTPrivateKey");
                            var fundsAmount = args.GetValue<ulong>("FundsAmount");
                            var unitOfAmount = args.GetValue<int>("UnitOfAmount");
                            response = ApiResponse.CreateApiResponseWithResult<string>(
                                            await GeneralLedgerDefundWalletAsync(userSessionToken, subjectSessionToken,
                                                            dltAddress, dltPrivateKey, fundsAmount, unitOfAmount).ConfigureAwait(false));
                        }
                        break;

                    case ApiIdentifier.GeneralLedgerTransferFunds:
                        {
                            // This operation takes a "context" UserSessionToken, a "from" SessionToken, a "to" SessionToken, 
                            // and a positive integer Funds Amount value 
                            // and returns a string
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("FromSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("ToUserName", typeof(string)));
                            validParams.Add(new ApiParameterValidation("FundsAmount", typeof(ulong)));
                            validParams.Add(new ApiParameterValidation("UnitOfAmount", typeof(int)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var fromSessionToken = args.GetValue<string>("FromSessionToken");
                            var toUserName = args.GetValue<string>("ToUserName");
                            var fundsAmount = args.GetValue<ulong>("FundsAmount");
                            var unitOfAmount = args.GetValue<int>("UnitOfAmount");
                            response = ApiResponse.CreateApiResponseWithResult<string>(
                                            await GeneralLedgerTransferFundsAsync(userSessionToken, fromSessionToken,
                                                            toUserName, fundsAmount, unitOfAmount).ConfigureAwait(false));
                        }
                        break;

                    case ApiIdentifier.GeneralLedgerPurchaseContent:
                        {
                            // This operation takes a "context" UserSessionToken, a "buyer" SessionToken, a ConentDIDRef,
                            // and a positive price (in US$) 
                            // and returns a string
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("BuyerSessionToken", typeof(string)));  
                            validParams.Add(new ApiParameterValidation("ContentDIDRef", typeof(string)));
                            validParams.Add(new ApiParameterValidation("Price", typeof(decimal)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var buyerSessionToken = args.GetValue<string>("BuyerSessionToken");
                            var contentDIDRef = args.GetValue<string>("ContentDIDRef");
                            var price = args.GetValue<decimal>("Price");
                            response = ApiResponse.CreateApiResponseWithResult<string>(
                                            await GeneralLedgerPurchaseContentAsync(userSessionToken, buyerSessionToken,
                                                            contentDIDRef, price).ConfigureAwait(false));
                        }
                        break;

                    #endregion 


                    #region Spawn Apis
                    case ApiIdentifier.SpawnNode:
                        {
                            // This operation takes a NodeType,
                            // and returns a NodeDownloadUrl
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("NodeType", typeof(string)));
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var nodeType = args.GetValue<string>("NodeType");
                            response = ApiResponse.CreateApiResponseWithResult<string>(await SpawnNodeAsync(nodeType).ConfigureAwait(false));
                        }
                        break;
                    #endregion 


                    #region Published Content Apis
                    //             case ApiIdentifier.GetPublishedContentStatus:
                    //                 {
                    //                     #region Validate arguments passed in
                    //                     // Declare required arguments
                    //                     List<ApiParameterValidation> validParams = new List<ApiParameterValidation>
                    //                     {
                    //	new ApiParameterValidation("UserAccessToken", typeof(string)),
                    //                         new ApiParameterValidation("LocalFilePath", typeof(string)),
                    //                         new ApiParameterValidation("UserSid", typeof(string))
                    //                     };

                    //                     // Retrieve the received validated arguments from the request 
                    //                     var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                    //var userSSIDAccessToken = args.GetValue<string>("UserAccessToken");
                    //var UserSid = args.GetValue<string>("UserSid");
                    //var LocalFilePath = args.GetValue<string>("LocalFilePath");
                    //if (userSSIDAccessToken == "")
                    //{
                    //	throw new UnoSysArgumentException("Parameter 'UserAccessToken' must not be empty.");
                    //}
                    //if (UserSid == "")
                    //{
                    //	throw new UnoSysArgumentException("Parameter 'UserSid' must not be empty.");
                    //}
                    //if (LocalFilePath == "")
                    //{
                    //	throw new UnoSysArgumentException("Parameter 'LocalFilePath' must not be empty.");
                    //}
                    //#endregion

                    //#region Process the Request
                    //var userSession = sessionManager.ResolveSsidSession(userSSIDAccessToken);
                    //// Create an ApiResponse with a string Result
                    //var pcReceiptJson = overlayManager.GetPublishedContentStatus( userSession.AccessToken, SDID, LocalFilePath, UserSid).Result;
                    //if( !string.IsNullOrEmpty(pcReceiptJson) ) // Found?
                    //                     {
                    //	response = ApiResponse.CreateApiResponseWithResult<string>(pcReceiptJson);
                    //}
                    //else
                    //                     {
                    //	Debug.Print("No associated WC SSID found for this OS idenitty - log into the World Computer then try again.");
                    //	throw new UnauthorizedAccessException("No associated WC SSID found for this OS idenitty - log into the World Computer then try again.");
                    //}
                    //                     #endregion 
                    //                 }
                    //                 break;

                    //case ApiIdentifier.PublishContent:
                    //                   {
                    //		#region Validate arguments passed in
                    //		// Declare required arguments
                    //		List<ApiParameterValidation> validParams = new List<ApiParameterValidation>
                    //		{
                    //			new ApiParameterValidation("UserAccessToken", typeof(string)),
                    //			new ApiParameterValidation("PublishedContentDefinitionJson", typeof(string)),
                    //		};

                    //		// Retrieve the received validated arguments from the request 
                    //		var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                    //		var userSSIDAccessToken = args.GetValue<string>("UserAccessToken");
                    //		var pcDefJson = args.GetValue<string>("PublishedContentDefinitionJson");
                    //		if (userSSIDAccessToken == "")
                    //		{
                    //			throw new UnoSysArgumentException("Parameter 'UserAccessToken' must not be empty.");
                    //		}
                    //		if (pcDefJson == null!)
                    //		{
                    //			throw new UnoSysArgumentException("Parameter 'PublishedContentDefinitionJson' must not be empty.");
                    //		}
                    //		#endregion

                    //		#region Process the Request
                    //		var userSession = sessionManager.ResolveSsidSession(userSSIDAccessToken);
                    //		var pcDef = JsonSerializer.Deserialize<PublishedContentDefinition>(pcDefJson);
                    //		Debug.Print($"ApiManager.PublishContent() - Title={pcDef.Title} File={pcDef.LocalFilePath}");
                    //		var pcReceiptJson = overlayManager.PublishContent( userSession.AccessToken, pcDefJson).Result;
                    //		if( !string.IsNullOrEmpty(pcReceiptJson))
                    //                       {
                    //			response = ApiResponse.CreateApiResponseWithResult<string>(pcReceiptJson);
                    //		}
                    //		#endregion
                    //	}
                    //	break;


      //              case ApiIdentifier.SetPublishPermissions:
      //                  {
						//	#region Validate arguments passed in
						//	// Declare required arguments
						//	List<ApiParameterValidation> validParams = new List<ApiParameterValidation>
						//	{
						//		new ApiParameterValidation("FileName", typeof(string)),
						//		new ApiParameterValidation("OsSidToAllow", typeof(string)),
						//		new ApiParameterValidation("ProcessIdToAllow", typeof(uint)),
						//		new ApiParameterValidation("ProcessNameToAllow", typeof(string))
						//	};
						//	// Retrieve the received validated arguments from the request 
						//	var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
						//	var fileName = args.GetValue<string>("FileName");
						//	var osSIDToAllow = args.GetValue<string>("OsSidToAllow");
						//	var processIdToAllow = args.GetValue<uint>("ProcessIdToAllow");
						//	var processNameToAllow = args.GetValue<string>("ProcessNameToAllow");
						//	if (fileName == "")
						//	{
						//		throw new UnoSysArgumentException("Parameter 'FileName' must not be empty.");
						//	}
						//	if (osSIDToAllow == "")
						//	{
						//		throw new UnoSysArgumentException("Parameter 'OsSIDToAllow' must not be empty.");
						//	}
						//	if (processIdToAllow <= 0)
						//	{
						//		throw new UnoSysArgumentException("Parameter 'ProcessIdToAllow' must be > 0");
						//	}
						//	if (processNameToAllow == "")
						//	{
						//		throw new UnoSysArgumentException("Parameter 'ProcessNameToAllow' must not be empty.");
						//	}
						//	#endregion
						//	publishedContentManager.RegisterPublishedFileContext(fileName, osSIDToAllow, processIdToAllow, processNameToAllow);
						//	//publishedContentManager.OsUserSIDToAllow = osSIDToAllow;
						//	//publishedContentManager.ProcessIDToAllow = processIdToAllow;
						//	//publishedContentManager.ProcessNameToAllow = processNameToAllow;
						//	//response = new ApiResponse();  // Empty 'OK' success response
						//	response = ApiResponse.CreateApiResponseWithResult<int>(SDID);
						//}
						//break;

					case ApiIdentifier.ResetPublishPermissions:
						{
							#region Validate arguments passed in
							// Declare required arguments
							List<ApiParameterValidation> validParams = new List<ApiParameterValidation>
							{
								new ApiParameterValidation("FileName", typeof(string)),
								new ApiParameterValidation("OsSidToAllow", typeof(string)),
								new ApiParameterValidation("ProcessIdToAllow", typeof(uint)),
								new ApiParameterValidation("ProcessNameToAllow", typeof(string))
							};
							// Retrieve the received validated arguments from the request 
							var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
							var fileName = args.GetValue<string>("FileName");
							var osSIDToAllow = args.GetValue<string>("OsSidToAllow");
							var processIdToAllow = args.GetValue<uint>("ProcessIdToAllow");
							var processNameToAllow = args.GetValue<string>("ProcessNameToAllow");
							if (fileName == "")
							{
								throw new UnoSysArgumentException("Parameter 'FileName' must not be empty.");
							}
							if (osSIDToAllow == "")
							{
								throw new UnoSysArgumentException("Parameter 'OsSIDToAllow' must not be empty.");
							}
							if (processIdToAllow <= 0)
							{
								throw new UnoSysArgumentException("Parameter 'ProcessIdToAllow' must be > 0");
							}
							if (processNameToAllow == "")
							{
								throw new UnoSysArgumentException("Parameter 'ProcessNameToAllow' must not be empty.");
							}
							#endregion
							publishedContentManager.UnRegisterPublishedFileContext(fileName, osSIDToAllow, processIdToAllow, processNameToAllow);
							//publishedContentManager.OsUserSIDToAllow = null!;
							//publishedContentManager.ProcessIDToAllow = -1;
							//publishedContentManager.ProcessNameToAllow = null!;
							response = new ApiResponse();  // Empty 'OK' success response
						}
						break;

					case ApiIdentifier.GetPublishedContentTypes:
                        {
							List<long> keys = null!;
							List<string> values = null!;
							DictToLists<long,string>(publishedContentManager.ContentType, out keys, out values);
							// Declare required return values
							List<ApiParameter> outputs = new List<ApiParameter>();
							outputs.Add(new ApiParameter("Keys", typeof(List<long>), JsonSerializer.Serialize<List<long>>(keys)));
							outputs.Add(new ApiParameter("Values", typeof(List<string>), JsonSerializer.Serialize<List<string>>(values)));
							response = new ApiResponse(outputs);
						}
						break;

					case ApiIdentifier.GetPublishedContentTypeHints:
						{
							List<long> keys = null!;
							List<string> values = null!;
							DictToLists<long, string>(publishedContentManager.ContentTypeHint, out keys, out values);
							// Declare required return values
							List<ApiParameter> outputs = new List<ApiParameter>();
							outputs.Add(new ApiParameter("Keys", typeof(List<long>), JsonSerializer.Serialize<List<long>>(keys)));
							outputs.Add(new ApiParameter("Values", typeof(List<string>), JsonSerializer.Serialize<List<string>>(values)));
							response = new ApiResponse(outputs);
						}
						break;


					case ApiIdentifier.GetPublishedCategories:
						{
							List<long> keys = null!;
							List<string> values = null!;
							DictToLists<long, string>(publishedContentManager.Category, out keys, out values);
							// Declare required return values
							List<ApiParameter> outputs = new List<ApiParameter>();
							outputs.Add(new ApiParameter("Keys", typeof(List<long>), JsonSerializer.Serialize<List<long>>(keys)));
							outputs.Add(new ApiParameter("Values", typeof(List<string>), JsonSerializer.Serialize<List<string>>(values)));
							response = new ApiResponse(outputs);
						}
						break;

					case ApiIdentifier.GetPublishedTopics:
						{
							List<long> keys = null!;
							List<string> values = null!;
							DictToLists<long, string>(publishedContentManager.Topic, out keys, out values);
							// Declare required return values
							List<ApiParameter> outputs = new List<ApiParameter>();
							outputs.Add(new ApiParameter("Keys", typeof(List<long>), JsonSerializer.Serialize<List<long>>(keys)));
							outputs.Add(new ApiParameter("Values", typeof(List<string>), JsonSerializer.Serialize<List<string>>(values)));
							response = new ApiResponse(outputs);
						}
						break;

					case ApiIdentifier.GetPublishedLanguages:
						{
							List<long> keys = null!;
							List<string> values = null!;
							DictToLists<long, string>(publishedContentManager.Language, out keys, out values);
							// Declare required return values
							List<ApiParameter> outputs = new List<ApiParameter>();
							outputs.Add(new ApiParameter("Keys", typeof(List<long>), JsonSerializer.Serialize<List<long>>(keys)));
							outputs.Add(new ApiParameter("Values", typeof(List<string>), JsonSerializer.Serialize<List<string>>(values)));
							response = new ApiResponse(outputs);
						}
						break;


                    //case ApiIdentifier.ComputePublishedFileName:
                    //	{
                    //		#region Validate arguments passed in
                    //		// Declare required arguments
                    //		List<ApiParameterValidation> validParams = new List<ApiParameterValidation>
                    //		{
                    //			new ApiParameterValidation("UserAccessToken", typeof(string)),
                    //			new ApiParameterValidation("DRMAccessType", typeof(int)),
                    //			new ApiParameterValidation("ContentType", typeof(int)),
                    //			new ApiParameterValidation("Category", typeof(int)),
                    //			new ApiParameterValidation("Topic", typeof(int)),
                    //			new ApiParameterValidation("Language", typeof(int)),
                    //			new ApiParameterValidation("Library", typeof(int)),
                    //			new ApiParameterValidation("Channel", typeof(int)),
                    //			new ApiParameterValidation("LocalFilePath", typeof(string))
                    //		};

                    //		// Retrieve the received validated arguments from the request 
                    //		var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                    //		var userSSIDAccessToken = args.GetValue<string>("UserAccessToken");
                    //		var drmAccessType = args.GetValue<int>("DRMAccessType");
                    //		var contentType = args.GetValue<int>("ContentType");
                    //		var category = args.GetValue<int>("Category");
                    //		var topic = args.GetValue<int>("Topic");
                    //		var language = args.GetValue<int>("Language");
                    //		var library = args.GetValue<int>("Library");
                    //		var channel = args.GetValue<int>("Channel");
                    //		var localFilePath = args.GetValue<string>("LocalFilePath");
                    //		if (userSSIDAccessToken == "")
                    //		{
                    //			throw new ArgumentExUnoSysArgumentExceptionception("Parameter 'UserAccessToken' must not be empty.");
                    //		}
                    //		if (localFilePath == "")
                    //		{
                    //			throw new UnoSysArgumentException("Parameter 'LocalFilePath' must not be empty.");
                    //		}
                    //		#endregion

                    //		#region Process the Request
                    //		var userSession = sessionManager.ResolveSsidSession(userSSIDAccessToken);
                    //		if (userSession != null && !string.IsNullOrEmpty(userSession.AccessToken))
                    //		{
                    //			// %TODO% - validate the userSession!!!
                    //			var vDriveLetter = nodeInstallationContext.FileSystemLetter;
                    //			var publishedFilePath =  nodeInstallationContext.NodePublicVDriveRoot;  // W:\WCOPublic
                    //			publishedFilePath = Path.Combine(publishedFilePath, (drmAccessType == (int)DRMAccessType.FREE ? "Free" : "Premium"));
                    //			Debug.Print($"contenttype={contentType}, category={category}, vDriveLetter={vDriveLetter}");
                    //			publishedFilePath = Path.Combine(publishedFilePath, publishedContentManager.ContentType[contentType],
                    //																publishedContentManager.Category[category],
                    //																publishedContentManager.Topic[topic]);
                    //			publishedFilePath = Path.Combine(publishedFilePath, Path.GetFileNameWithoutExtension(localFilePath) +
                    //													"." + language.ToString() + "." + library.ToString() + "." +
                    //													channel.ToString() + Path.GetExtension(localFilePath) + ".{0}");
                    //			Debug.Print($"publishedFilePath={publishedFilePath}");
                    //			publishedFilePath = vDriveLetter + publishedFilePath;
                    //			//Debug.Print($"publishedFilePath={publishedFilePath}, AFTER");
                    //			response = ApiResponse.CreateApiResponseWithResult<string>(publishedFilePath);
                    //		}
                    //		else
                    //                       {
                    //			throw new UnauthorizedAccessException();
                    //                       }
                    //		#endregion
                    //	}
                    //	break;
                    #endregion


                    #region ATTN Apis
                    //	case ApiIdentifier.TreasuryTransfer:
                    //		{
                    //			#region Validate arguments passed in
                    //			// Declare required arguments
                    //			/*
                    //pList.Add(new ApiParameter("UserAccessToken", typeof(string), userAccessToken));
                    //            pList.Add(new ApiParameter("UserSid", typeof(string), JsonConvert.SerializeObject(ssid)));
                    //            pList.Add(new ApiParameter("FromTreasury", typeof(bool), JsonConvert.SerializeObject(fromTreasury)));
                    //            pList.Add(new ApiParameter("Amount", typeof(uint), JsonConvert.SerializeObject(amount)));
                    //			 * */
                    //			List<ApiParameterValidation> validParams = new List<ApiParameterValidation>
                    //			{
                    //				new ApiParameterValidation("AppAccessToken", typeof(string)),
                    //				new ApiParameterValidation("UserSid", typeof(string)),
                    //				new ApiParameterValidation("FromTreasury", typeof(bool)),
                    //				new ApiParameterValidation("Amount", typeof(uint)),
                    //			};
                    //			// Retrieve the received validated arguments from the request 
                    //			var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                    //			var appSSIDAccessToken = args.GetValue<string>("AppAccessToken");
                    //			var UserSid = args.GetValue<string>("UserSid");
                    //			var FromTreasury = args.GetValue<bool>("FromTreasury");
                    //			var Amount = args.GetValue<uint>("Amount");
                    //			if (appSSIDAccessToken == "")
                    //			{
                    //				throw new UnoSysArgumentException("Parameter 'AppAccessToken' must not be empty.");
                    //			}
                    //			if (UserSid == "")
                    //			{
                    //				throw new UnoSysArgumentException("Parameter 'UserSid' must not be empty.");
                    //			}
                    //			if( Amount == 0)
                    //                        {
                    //				throw new UnoSysArgumentException("Parameter 'Amount' must not be zero.");
                    //			}
                    //			#endregion

                    //			#region Process the Request
                    //			var appSession = sessionManager.ResolveSsidSession(appSSIDAccessToken);
                    //			var receiptJson = overlayManager.TreasuryTransferTokensAsync(appSession.AccessToken, UserSid, FromTreasury, Amount).Result;
                    //			if (!string.IsNullOrEmpty(receiptJson))
                    //			{
                    //				response = ApiResponse.CreateApiResponseWithResult<string>(receiptJson);
                    //			}
                    //			#endregion
                    //		}
                    //		break;
                    #endregion

                    #region VirtualDisk Apis
                    case ApiIdentifier.VirtualDiskCreate:
                        {
                            // This operation takes a UserSessionToken and an opaque SubjectSessionToken - which can either be a User or an Application - along with
                            // an integer ClusterSize and an integer ReplicationFactor, and returns an opaque VirtualDiskSessionToken
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("SubjectSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("ClusterSize", typeof(int)));
                            validParams.Add(new ApiParameterValidation("ReplicationFactor", typeof(int)));  
                            
                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var subjectSessionToken = args.GetValue<string>("SubjectSessionToken");
                            var clusterSize = args.GetValue<int>("ClusterSize");
                            var replicationCount = args.GetValue<int>("ReplicationFactor");
                            response = ApiResponse.CreateApiResponseWithResult<string>(
                                            await VirtualDiskCreateAsync(userSessionToken, subjectSessionToken, clusterSize, replicationCount).ConfigureAwait(false));
                        } 
                        break;
                    case ApiIdentifier.VirtualDiskDelete:
                        {
                            // This operation takes a UserSessionToken and an opaque VirtualDiskSessionToken,
                            // and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("VirtualDiskSessionToken", typeof(string)));

                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var virtualDiskSessionToken = args.GetValue<string>("VirtualDiskSessionToken");
                            await VirtualDiskDeleteAsync(userSessionToken, virtualDiskSessionToken).ConfigureAwait(false);
                            response = new ApiResponse();  // Empty 'OK' success response

                        }
                        break;
                    case ApiIdentifier.VirtualDiskMount:
                        {
                            // This operation takes a UserSessionToken and an opaque VirtualDiskSessionToken,
                            // and returns an opaque VolumeSessionToken  
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("VirtualDiskSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("BlockSize", typeof(uint)));

                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var virtualDiskSessionToken = args.GetValue<string>("VirtualDiskSessionToken");
                            var blockSize = args.GetValue<uint>("BlockSize");
                            response = ApiResponse.CreateApiResponseWithResult<string>(
                                            await VirtualDiskMountAsync(userSessionToken, virtualDiskSessionToken, blockSize).ConfigureAwait(false));
                        }
                        break;
                    case ApiIdentifier.VirtualDiskUnmount:
                        {
                            // This operation takes a UserSessionToken and an opaque VolumeSessionToken,
                            // and returns nothing
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("VolumeSessionToken", typeof(string)));

                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var volumeSessionToken = args.GetValue<string>("VolumeSessionToken");
                            await VirtualDiskUnmountAsync(userSessionToken, volumeSessionToken).ConfigureAwait(false);
                            response = new ApiResponse();  // Empty 'OK' success response

                        }
                        break;
                    case ApiIdentifier.VirtualDiskVolumeMetaDataOperation:   // %TODO%  !!!!!!!!!! MUST Remove this API eventually as it is a vector to SPAM the WorldComputer network!!!!!!!!!!!!!!!!!!!!
                        {
                            // This operation takes a UserSessionToken and an opaque Operation string,
                            // and returns a string
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("Operation", typeof(string)));

                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var operation = args.GetValue<string>("Operation");
                            response = ApiResponse.CreateApiResponseWithResult<string>( 
                                        await VirtualDiskVolumeMetaDataOperationAsync(userSessionToken, operation).ConfigureAwait(false));
                            //response = new ApiResponse();  // Empty 'OK' success response

                        }
                        break;
                    case ApiIdentifier.VirtualDiskVolumeDataOperation:   // %TODO%  !!!!!!!!!! MUST Remove this API eventually as it is a vector to SPAM the WorldComputer network!!!!!!!!!!!!!!!!!!!!
                        {
                            // This operation takes a UserSessionToken and an opaque Operation string,
                            // and returns a string
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("Operation", typeof(string)));

                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var operation = args.GetValue<string>("Operation");
                            response = ApiResponse.CreateApiResponseWithResult<string>(
                                        await VirtualDiskVolumeDataOperationAsync(userSessionToken, operation).ConfigureAwait(false));

                        }
                        break;
                    case ApiIdentifier.VirtualDiskGetTopology:
                        {
                            // This operation takes a UserSessionToken and an opaque VirtualDiskSessionToken,
                            // and returns a string containing a serialized Virtual Disk topology
                            //
                            // Declare required arguments
                            List<ApiParameterValidation> validParams = new List<ApiParameterValidation>();
                            validParams.Add(new ApiParameterValidation("UserSessionToken", typeof(string)));
                            validParams.Add(new ApiParameterValidation("VirtualDiskSessionToken", typeof(string)));

                            // Retrieve the received validated arguments from the request 
                            var args = ApiRequest.GetValidInputs(validParams, jsonRequest);
                            var userSessionToken = args.GetValue<string>("UserSessionToken");
                            var virtualDiskSessionToken = args.GetValue<string>("VirtualDiskSessionToken");
                            response = ApiResponse.CreateApiResponseWithResult<string>(
                                            await VirtualDiskGetTopologyAsync(userSessionToken, virtualDiskSessionToken).ConfigureAwait(false));
                        }
                        break;


                    #endregion

                    default:
						break;
				}
			}
            #region Exceptions
            catch (Exception ex)
            {
                ProcessException(ex, out response, out resultcode, out resultmessage );
            }
            watch.Stop();
            //_logger.LogInformation($"Api: {((ApiIdentifier)apiid).ToString()} [{watch.Elapsed.TotalMilliseconds} ms]");
            statisticsManager.RecordStatisticMeasurement((StatisticMeasurementType)apiid, timeManager.ProcessorUtcTimeInTicks, watch.Elapsed.TotalMilliseconds);
            #endregion

            return new ApiResult
			{
				ResultCode = resultcode,
				ResultMessage = resultmessage,
				ApiResponseJson = ApiResponse.GetApiResponseJson(response)
			};
		}

        #region Helpers
        private string LocalStorePathAbsoutePath
        { get
            {
                string vDiskRootFolderName = null!;
                if (GlobalPropertiesContext.IsSimulatedNode())
                {
                    vDiskRootFolderName = $"Node{GlobalPropertiesContext.ThisNodeNumber()}_Root_{wcContext.WorldComputerVirtualDisk!.ID.ToString("N").ToUpper()}";
                }
                else
                {
                    vDiskRootFolderName = $"{localNodeContext!.NodeDIDRef.ToString("N").ToUpper()}_Root_{wcContext.WorldComputerVirtualDisk!.ID.ToString("N").ToUpper()}";
                }
                return Path.Combine(Path.Combine(nodeDirectoryPath, localNodeContext!.LocalStoreDirectoryName), vDiskRootFolderName);
            }
        }

        private void ProcessException( Exception ex, out ApiResponse response, out int resultcode, out string resultmessage)
        {
            response = null!;
            resultcode = -1;
            resultmessage = "";
            if (ex is UnoSysResourceNotFoundException)
            {
                response = new ApiResponse(((int)HttpStatusCode.NotFound));
            }
            else if (ex is UnoSysResourceAlreadyDefinedException)
            {
                response = new ApiResponse(((int)HttpStatusCode.Conflict), $"{ex.Message}");
                resultmessage = ex.Message;
            }
            else if (ex is AggregateException)
            {
                if (((AggregateException)ex).InnerExceptions.Count > 0) // && agex.InnerExceptions[0] is UnoSysUnauthorizedAccessException)
                {
                    ProcessException( ((AggregateException)ex).InnerExceptions[0], out response, out resultcode, out resultmessage);
                }
                else
                {
                    response = new ApiResponse(((int)HttpStatusCode.InternalServerError));
                    resultmessage = ex.Message;
                }
            }
            else if (ex is UnoSysUnauthorizedAccessException)
            {
                response = new ApiResponse(((int)HttpStatusCode.Unauthorized));
            }
            else if (ex is UnoSysArgumentException )
            {
                response = new ApiResponse(((int)HttpStatusCode.BadRequest), $"{ex.Message}");
                resultmessage = ex.Message;
            }
            else if(ex is UnoSysConflictException )
            {
                response = new ApiResponse(((int)HttpStatusCode.Conflict), $"{ex.Message}");
                resultmessage = ex.Message;
            }
            else if(ex is FormatException )
            {
                response = new ApiResponse(((int)HttpStatusCode.BadRequest), $"{ex.Message}");
                resultmessage = ex.Message;
            }
            else if(ex is CryptographicException)
            {
                response = new ApiResponse(((int)HttpStatusCode.Unauthorized));
            }
            else if( ex is KeyNotFoundException)
            {
                response = new ApiResponse(((int)HttpStatusCode.BadRequest), $"{ex.Message}");
                resultmessage = ex.Message;
            }
            else if (ex is DLTInsufficientBankFunds)
            {
                response = new ApiResponse(((int)HttpStatusCode.Conflict), $"Insufficient funds in Bank to complete operation.");
                resultmessage = ex.Message;
            }
            else if( ex is DLTTransactionException)
            {
                response = new ApiResponse(((int)HttpStatusCode.Conflict), $"{ex.Message}");
                resultmessage = ex.Message;
            }
            else
            {
                // Default catch all
                response = new ApiResponse(((int)HttpStatusCode.InternalServerError));
            }
        }


        private ApiResponse SetApiResponseFromTableSessionToken(string tableSessionToken)
        {
            return ApiResponse.CreateApiResponseWithResult<string>(tableSessionToken);
        }
  //          ITableState table = null!;
  //          var tSessionToken = new TableSessionToken( tableSessionToken );
  //          if (tableStateRegistry.ContainsKey(tSessionToken) )
  //          {
  //              table = tableStateRegistry[tSessionToken];
  //          }
  //          else
  //          {
  //              tableStateRegistry.AddTableSessionToken(tSessionToken, tableState);
  //          }
  ////	ITableState table = tableStateRegistry[tableSessionToken];
  //          return ApiResponse.CreateApiResponseWithResult<TableOperationResponse>(new TableOperationResponse
  //          {
  //              ResponseCode = TableOperationResponseCode.OK,
  //              //TableSessionToken = table.ID,
  //              CurrentRecord = table.CurrentRecordOrdinal,
  //              IsBoT = table.IsBoT,
  //              IsEoT = table.IsEoT,
  //              IsDeleted = table.IsDeleted,
  //              IsDirty = table.IsDirty
  //          });
  //      }


        private void ThrowIfParameterNullOrEmpty(string context, string value )
		{
            if (string.IsNullOrEmpty(value))
            {
                throw new UnoSysArgumentException($"Parameter '{context}' must not be null or empty.");
            }
        }

        private void ThrowIfParameterNotPositiveFundAmount( string context, ulong fundsAmount )
        {
            if( fundsAmount == 0)
            {
                throw new UnoSysArgumentException($"Parameter '{context}' must be > 0.");
            }
        }

        private void ThrowIfParameterNotPositivePrice(string context, decimal price)
        {
            if (price <= 0.00m)
            {
                throw new UnoSysArgumentException($"Parameter '{context}' must be > 0.00");
            }
        }

        private DateTime ThrowIfParameterNotLegalDate(string context, string date )
        {
            DateTime legalDate = DateTime.MinValue;
            if (!DateTime.TryParseExact(date, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out legalDate))
            {
                throw new UnoSysArgumentException($"Parameter {context} must be in the form YYYYMMDD");
            }
            return legalDate;
        }

        private void ThrowIfEqual(string context, string value1, string value2)
        {
            if (value1.Equals(value2,StringComparison.Ordinal))
            {
                throw new UnoSysArgumentException($"Parameters {context} must not be equal.");
            }
        }

        private void ThrowIfParameterNoValidIDString(string context, string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32)
            {
                throw new UnoSysArgumentException($"Parameter '{context}' is not a valid ID.");
            }
        }

        private void ThrowIfParameterNoValidSessionTokenString(string context, string value, SessionType sessionType = SessionType.Undefined)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 33)
            {
                throw new UnoSysArgumentException($"Parameter '{context}' is not a valid SessionToken.");
            }
            if( sessionType != SessionType.Undefined )
            {
                if( SessionToken.GetSessionTokenType(value) != sessionType)
                {
                    throw new UnoSysArgumentException($"Parameter '{context}' is the wrong Session Token type.");
                }
            }
        }

        private void ThrowIfParameterIsNotLegalIdentifier( string context, string identifier )
        {
            ThrowIfParameterNullOrEmpty(context, identifier);
            if (!Utilities.IsValidCSharpIdentifier(identifier))
            {
                throw new UnoSysArgumentException($"Parameter '{context}' not a valid resource identifier.");
            }
        }

        private void ThrowIfParameterNotInInclusiveIntegerRange(string context, int value, int inclusiveLowerBound, int inclusiveUpperBound)
        {
            if (value < inclusiveLowerBound || value > inclusiveUpperBound)
            {
                throw new UnoSysArgumentException($"Parameter '{context}' must be between {inclusiveLowerBound} and {inclusiveUpperBound} inclusively.");
            }
        }



        private void DictToLists<K,V>( Dictionary<K,V> dict, out List<K> keys, out List<V> vals)
        {
			keys = null!;
			vals = null!;
			if( dict != null && dict.Count > 0)
            {
				keys = new List<K>(dict.Count);
				vals = new List<V>(dict.Count);
				foreach( var kvp in dict)
                {
					keys.Add(kvp.Key);
					vals.Add(kvp.Value);
                }
            }
        }
		#endregion
	}

	internal class ApiResult
	{
		internal ApiResult()
		{
			ApiResponseJson = "";
			ResultMessage = "Ok";
			ResultCode = 200;
		}
		internal string ApiResponseJson { get; init; }
		internal string ResultMessage { get; init; }
		internal int ResultCode { get; init; }
	}

	internal class ApiStartup
	{
		//readonly string WorldComputerApiOrigins = "WorldComputerApiOrigins";
		//readonly string WithOriginsTemplate = null!;
		//readonly string ApiUrl = null!;
		ApiManager apiManager = null!;
		readonly IWorldComputerContext wcContext = null!;
        readonly ILocalNodeContext localNodeContext = null!;

        [MethodImpl(MethodImplOptions.NoInlining)]
		public ApiStartup( ILocalNodeContext localnodecontext, IWorldComputerContext wccontext, ApiManager apimanager )//, string withOriginsTemplate )//, string apiUrl)
		{
			apiManager = apimanager;
			//WithOriginsTemplate = withOriginsTemplate;
			//ApiUrl = apiUrl;
            localNodeContext = localnodecontext;
			wcContext = wccontext;

        }
		[MethodImpl(MethodImplOptions.NoInlining)]
		public ApiStartup(IConfiguration configuration)
		{
			Configuration = configuration;
		}

		public IConfiguration? Configuration { get; }


		[MethodImpl(MethodImplOptions.NoInlining)]
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddMvc().AddControllersAsServices();
			services.AddSingleton(new UnoSysApiController( apiManager, localNodeContext, wcContext));
			/*
			services.AddCors(options =>
			{
				options.AddPolicy(name: WorldComputerApiOrigins,
								  builder =>
								  {
									  //builder.WithOrigins($"{string.Format(Firmware.kernelOptions.ShellBaseUrlTemplate,EntryPoint.ComputeBasePort(Firmware.kernelOptions.NodeControllerPort))}, {string.Format(Firmware.kernelOptions.ShellBaseUrlTemplate, EntryPoint.ComputeBasePort(Firmware.kernelOptions.NodeControllerPort) + 1)}")
									  builder.WithOrigins(WithOriginsTemplate)
									  .AllowAnyMethod()
									  .WithHeaders(HeaderNames.ContentType, HeaderNames.Authorization);
								  });
			});*/

			//services.AddControllers();
			/*services.AddAuthentication("Bearer");
			
			   .AddIdentityServerAuthentication("Bearer", options =>
			   {
				   options.ApiName = "api1";                       // API Resource name
																   //options.Authority = string.Format(Firmware.kernelOptions.InternalStsBaseUrlTemplate,EntryPoint.ComputeBasePort(Firmware.kernelOptions.NodeControllerPort) + 3);  // URL of API
				   options.Authority = ApiUrl;  // URL of API
				   options.RequireHttpsMetadata = true;
				   options.JwtBackChannelHandler = GetHandler(); // validate the Server's cert
			   });*/
			
		}

		//private static HttpClientHandler GetHandler()
		//{
		//	var handler = new HttpClientHandler();
		//	handler.ClientCertificateOptions = ClientCertificateOption.Manual;
		//	handler.SslProtocols = SslProtocols.None;
		//	handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
		//	return handler;
		//}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			
			if (env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
				//IdentityModelEventSource.ShowPII = true;  // List line exposes more detailed IdentityServer error information
			}
			app.UseHsts();
			app.UseHttpsRedirection();
			app.UseRouting();
			//app.UseCors(WorldComputerApiOrigins);
			app.UseAuthentication();
			app.UseAuthorization();
			
			app.UseEndpoints(endpoints =>
			{
				endpoints.MapControllers();
			});		
		}
	}


	[ApiController]
    //[Route("FirmwareApi")]
    [Route("UnoSysApi")]
    public class UnoSysApiController : ControllerBase
	{
		ApiManager apiManager = null!;
		IWorldComputerContext wcContext = null!;
        ILocalNodeContext localNodeContext = null!;
        // NOTE:  Required to be typed as object rather than ApiManager because that class is "internal" in scope whereas this class must be public
        public UnoSysApiController( object apimanager, ILocalNodeContext localnodecontext, IWorldComputerContext wccontext) 
        {
            apiManager = (ApiManager)apimanager;
			wcContext = wccontext;
            localNodeContext = localnodecontext;
		}

		
		[HttpPost]
		public async Task<IActionResult> Post([FromBody] ApiRequest request)
		{
#if !WCNODE_VS_BUILD
            if (Debugger.IsAttached)
            {
                return StatusCode(500,"UnoSys immunity defenses activitated - not processing Api calls at this time.");  // Return HTTP status 500 to the client
            }
#endif
            ApiResponse apiResponse = null!;
			try
			{
				ApiResult apiResult = await apiManager.ApiRouteToImplementationAsync(request.ApiId, request.Verb,
																	request.RequestFormat, ApiRequest.GetApiRequestJson(request)).ConfigureAwait(false);

				if (apiResult != null && !string.IsNullOrEmpty(apiResult.ApiResponseJson))
				{
					var response = ApiResponse.GetApiResponse(apiResult.ApiResponseJson);
					if (IsSuccessfulCall(apiResult.ResultCode, response))
					{
						apiResponse = await Task.FromResult<ApiResponse>(response);
					}
					else
					{
						if (response.StatusCode == 200)
						{
							return StatusCode(apiResult.ResultCode);
						}
						else
						{
							return StatusCode(response.StatusCode, response.StatusMessage);
						}
					}
				}
				else
				{
					return StatusCode(500);
				}
			}
			catch (Exception ex)
			{
				return BadRequest(ex.Message);
			}
			if (apiResponse != null!)
			{
				return Content(HostUtilities.JsonSerialize<ApiResponse>(apiResponse), "application/json");
			}
			else
			{
				return StatusCode(500);
			}
		}

		[HttpGet]
		public async Task<IActionResult> Get([FromQuery(Name = "v")] string v)
		{
#if !WCNODE_VS_BUILD
            if (Debugger.IsAttached)
            {
                return StatusCode(500, "UnoSys immunity defenses activitated - not processing Api calls at this time.");  // Return HTTP status 500 to the client
            }
#endif
            // This method validates the passed in server certificate issuer's organizational unit is 'this' WCNode
            bool result = false;
			if (!string.IsNullOrEmpty(v))
			{
				try
				{
                    var decryptedOrgUnit = HostCryptology.AsymmetricDecryptionWithoutCertificate(
                           HostCryptology.ConvertHexStringToBytes(v), localNodeContext.Node2048AsymmetricPrivateKey);
                    result = localNodeContext.NodeID.Equals(new Guid(decryptedOrgUnit));
                }
				catch (Exception ex)
				{
                    Debug.Print($"Error in FirmwareApiController.Get() - Ignored\n{ex}");
                    return StatusCode(500);  // Return HTTP status 500 to the client.  
                }
            }
            await Task.CompletedTask;
            return Content(result.ToString(), "application/text");
		}

		#region Helpers
		private bool IsSuccessfulCall(int resultCode, IApiResponse response)
		{
			return resultCode == 0 && response.StatusCode == 200;  // for now this is the only definition of a successful call
		}
		#endregion
	}

    internal class ImmuniityCheck
    {
        internal ImmuniityCheck() { }  // Empty on purpose - do not remove!!!
    }
}
