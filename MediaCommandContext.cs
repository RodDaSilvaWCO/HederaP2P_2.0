using Microsoft.Extensions.Hosting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Unosys.SDK;
using UnoSys.Api.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WorldComputer.Simulator
{

    public class MediaCommandContext : ICommandContext
	{
        #region Field Members
        const int DEFAULT_CLIENT_NODE_NUMBER = 1;
        const int MAX_TITLE_LENGTH = 200;

        Hashtable switchSet = new Hashtable( 20 );
		string[] cmdArgs;
        string userName = null!;
        bool userSwitch = false;
        bool publishSwitch = false;
        bool accessSwitch = false;
        bool clientSimulator = false;
        bool simulatorHelp = false;
        bool priceSwitch = false;
        string contentPrice = null!;
        string contentFile = null!;
        string contentTitle = null!;
        NetworkSpec NetworkSpec = null!;
        VDriveInfo VirtualDriveSpec = null!;
        int clientNodeNumber = DEFAULT_CLIENT_NODE_NUMBER;

		#endregion

		#region Constructors
		public MediaCommandContext( string[] commandContextArgs )
		{
            cmdArgs = commandContextArgs;
			// Define valid context switches
			string[] allowedSwitches = new string[6];
			allowedSwitches[0] = "/PUBLISH:";
            allowedSwitches[1] = "/PRICE:";
            allowedSwitches[2] = "/ACCESS";
            allowedSwitches[3] = "/CLIENTNODE:";
            allowedSwitches[4] = "/USER:";
            allowedSwitches[5] = "/?";
			CommandLineTool.ParseCommandContextSwitches( commandContextArgs, allowedSwitches, switchSet, this );
			simulatorHelp = (bool) switchSet["/?"];
		}
		#endregion

		#region Public Methods
		public void ProcessCommand()
		{
			if (simulatorHelp)
			{
				CommandLineTool.DisplayBanner();
				DisplayUsage();
			}
			else
			{
                Program.UnoSysApiConnection = new UnoSysConnection(string.Format(Program.UnoSysApiUrlTemplate,Program.BasePort + ((clientNodeNumber - 1) * Program.BasePortSpacing)));

                #region Publish content
                if (publishSwitch)
                {
                   PublishContent();
                }
                #endregion

                #region Access content
                if (accessSwitch)
                {
                    AccessContent();
                }
                #endregion
            }
        }

        public bool ValidateContext()
        {
            bool invalidSwitch = false;
            object switchValue = null;

            #region Switch Processing
            #region Publish switch processing
            //Examples of possible legal switch usage: /PUBLISH
            switchValue = switchSet["/PUBLISH"];
            if (switchValue is bool)
            {
                publishSwitch = (bool)switchValue;
            }
            #endregion

            #region Access switch processing
            //Examples of possible legal switch usage: /ACCESS
            switchValue = switchSet["/ACCESS"];
            if (switchValue is bool)
            {
                accessSwitch = (bool)switchValue;
            }
            #endregion

            //#region Price switch processing
            //switchValue = switchSet["/PRICE"];
            //if (switchValue is string)
            //{
            //    if (!string.IsNullOrEmpty((string)switchValue))
            //    {
            //        string[] values = ((string)switchValue).Split(new char[] { ',' }, StringSplitOptions.None); // NOTE: We do NOT throw away empty values in order to check for invalid switches
            //        if (values.Length != 1)
            //        {
            //            invalidSwitch = true;
            //        }
            //        else
            //        {
            //            priceAmount = values[0];
            //            priceSwitch = true;
            //        }
            //        if (invalidSwitch)
            //        {
            //            throw new CommandLineToolInvalidSwitchException("/PRICE:" + switchValue);
            //        }
            //    }
            //}
            //else
            //{
            //    if ((bool)switchValue)
            //    {
            //        throw new CommandLineToolInvalidSwitchException("/PRICE:" + switchValue);
            //    }
            //}
            //#endregion


            #region User switch processing
            switchValue = switchSet["/USER"];
            if (switchValue is string)
            {
                if (!string.IsNullOrEmpty((string)switchValue))
                {
                    string[] values = ((string)switchValue).Split(new char[] { ',' }, StringSplitOptions.None); // NOTE: We do NOT throw away empty values in order to check for invalid switches
                    if (values.Length != 1)
                    {
                        invalidSwitch = true;
                    }
                    else
                    {
                        userName = values[0];
                        userSwitch = true;
                    }
                    if (invalidSwitch)
                    {
                        throw new CommandLineToolInvalidSwitchException("/USER:" + switchValue);
                    }
                }
            }
            else
            {
                if ((bool)switchValue)
                {
                    throw new CommandLineToolInvalidSwitchException("/USER:" + switchValue);
                }
            }
            #endregion

            #region Client switch processing
            switchValue = switchSet["/CLIENTNODE"];
            if (switchValue is string)
            {
                if (!string.IsNullOrEmpty((string)switchValue))
                {
                    clientNodeNumber = int.Parse(((string)switchValue).ToUpper());
                }
                else
                {
                    clientNodeNumber = DEFAULT_CLIENT_NODE_NUMBER;
                }
            }
            else
            {
                if ((bool)switchValue)
                {
                    clientSimulator = true;
                    clientNodeNumber = DEFAULT_CLIENT_NODE_NUMBER;
                }
            }
            #endregion
            #endregion


            #region Invalid Switch Combination Check
            if (publishSwitch && accessSwitch)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/PUBLISH and /ACCESS cannot be used together.");
            }
            if (!publishSwitch && !simulatorHelp && !accessSwitch)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("One of /PUBLISH or /ACCESS is required.");
            }
            #endregion

            bool result = (publishSwitch || accessSwitch || userSwitch || priceSwitch ||  clientSimulator || simulatorHelp);
			if (!result)
			{
                CommandLineTool.DisplayBanner();
                this.DisplayUsage();
                throw new CommandLineToolInvalidCommandException("At least one switch must be specified.");
            }
            return result;
		}

		public void DisplayUsage()
		{
			Program.DisplaySuccessfulWriteLine( "WorldComputer MEDIA command usage:" );
            Program.DisplaySuccessfulWriteLine( "" );
            Program.DisplaySuccessfulWriteLine( "      WCSim MEDIA | M  [<switches>]" );
			Program.DisplaySuccessfulWriteLine( "" );
			Program.DisplaySuccessfulWriteLine( "where <switches> are one or more of:" );
			Program.DisplaySuccessfulWriteLine( "" );
            Program.DisplaySuccessfulWriteLine("  /ACCESS\t\t\t\tAccess a media file");
            Program.DisplaySuccessfulWriteLine($" /CLIENTNODE:<Node#>\t\t\tClient Node # to connect to - ({DEFAULT_CLIENT_NODE_NUMBER})");
            Program.DisplaySuccessfulWriteLine("  /PRICE:<Amount>\t\t\tPrice to access media file in USD - (free)");
            Program.DisplaySuccessfulWriteLine( " /PUBLISH\t\t\t\tPublish a User's media file");
            Program.DisplaySuccessfulWriteLine( " /USER:<UserName>\t\t\tEstablish logged in User context to execute operation in");
            Program.DisplaySuccessfulWriteLine( " /?\t\t\t\t\tUsage information" );
			Program.DisplaySuccessfulWriteLine( "" );
            Program.DisplaySuccessfulWriteLine("=======================================================================================================");
        }
		#endregion


		#region Helpers
        private void PublishContent()
        {
            UserSessionInfo userSessionInfo = null!;

            #region  Step #1:  Check if a simulation has already been created and if noto error
            if (!Simulator.NetworkAlreadyCreated())
            {
                throw new CommandLineToolInvalidOperationException($"A World Computer node network does not exist. Use 'WCSim NODE /Create' to create one first.");
            }
            #endregion

            #region Step #2: Deserialize the SIMULATOR_SETTINGS_FILE_NAME settings file
            NetworkSpec = Simulator.GetNetworkSpec();//)JsonSerializer.Deserialize<NetworkSpec>(fs);
            //using (System.IO.FileStream fs = new System.IO.FileStream(System.IO.Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_SETTINGS_FILE_NAME), 
            //    System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
            //{
            //    NetworkSpec = (NetworkSpec)JsonSerializer.Deserialize<NetworkSpec>(fs);
            //}
            #endregion

            #region Step #3: Check if a node network is not already running and if so error
            if (!NetworkCommandContext.NetworkAlreadyRunning(NetworkSpec))
            {
                throw new CommandLineToolInvalidOperationException("A World Computer node network is not already running.  Use NODE /START to start one running one first.");
            }
            #endregion

            #region  Step #4:  Check if a vdrive has already been created and if so error
            if (!Simulator.VirtualDriveAlreadyCreated())
            {
                throw new CommandLineToolInvalidOperationException($"A World Computer Virtual Drive does not exist. Use 'VDRIVE /Create' to create a one first.");
            }
            #endregion

            #region Step #5: Deserialize the SIMULATOR_VDRIVE_FILE_NAME settings file
            VirtualDriveSpec = Simulator.GetVirtualDriveSpec();
            #endregion

            #region Step #4:  Check if user already logged in
            var userLoggedIn = Simulator.UserAlreadyLoggedIn(clientNodeNumber, userName);
            if (!userLoggedIn )
            {
                if (!string.IsNullOrEmpty(userName))
                {
                    throw new CommandLineToolInvalidOperationException($"Cannot perform operation because User {userName.ToUpper()} is not logged in.");
                }
                else
                {
                    throw new CommandLineToolInvalidOperationException($"Cannot perform operation because no User context is established - use /USER switch, or login as a User.");
                }
            }
            else
            {
                if (userLoggedIn)
                {
                    #region Step #5: Deserialize UserSessionInfo to obtain the Token of the Logged in User session
                    userSessionInfo = Simulator.GetUserSessionInfo(clientNodeNumber, userName);
                    if (userSessionInfo == null) 
                    {
                        throw new CommandLineToolInvalidOperationException($"Corrupted WCSim user file.");
                    }
                    userName = userSessionInfo.UserName;
                    #endregion
                }

                #region Step #6 - Prompt User for FileName 
                var ogrForegroundColor = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("");
                    Console.WriteLine("");
                    Console.WriteLine("Enter Media file path to Publish: ");
                    Console.ForegroundColor = ConsoleColor.White;
                    contentFile = Console.ReadLine();
                    if (!System.IO.File.Exists(contentFile))
                    {
                        throw new CommandLineToolInvalidSwitchException($"File to Publish: '{contentFile}' cannot be found.");
                    }
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("");
                    Console.WriteLine("Enter Title:");
                    Console.ForegroundColor = ConsoleColor.White;
                    contentTitle = Console.ReadLine();
                    if(string.IsNullOrEmpty(contentTitle))
                    {
                        contentTitle = "*** No Title Provided ***";
                    }
                    if(contentTitle.Length > MAX_TITLE_LENGTH)
                    {
                        contentTitle = contentTitle.Substring(0, MAX_TITLE_LENGTH);
                    }
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("");
                    Console.WriteLine("Enter USD Price (empty for 0.00): $");
                    Console.ForegroundColor = ConsoleColor.White;
                    contentPrice = Console.ReadLine();
                    if( string.IsNullOrEmpty(contentPrice))
                    {
                        contentPrice = "0.00";
                    }
                    else
                    {
                        contentPrice = contentPrice.Trim();
                        decimal price = 0.00m;
                        if(!decimal.TryParse(contentPrice, out price))
                        {
                            throw new CommandLineToolInvalidSwitchException($"Invalid Price format.");
                        }
                        int decPos = contentPrice.IndexOf(".");
                        if ( decPos >= 0 && decPos < contentPrice.Length - 3)
                        {
                            throw new CommandLineToolInvalidSwitchException($"Invalid Price format - cannot have more than 2 decimal places.");
                        }
                    }

                    #region  Step #7:  - Open file to Publish and Copy to Virtual Drive
                    using (System.IO.FileStream fs = new System.IO.FileStream(contentFile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                    {
                        // Create the content file's name on the Virtual Drive
                        var wcContentFileName = Guid.NewGuid().ToString("N").ToUpper() + contentPrice.PadLeft(16,' ').Replace(".","_") + "_" + contentTitle.Trim() + $".{userName}.cnt";
                        Console.WriteLine("");
                        Console.WriteLine("Publishing your Media Content... One Moment Please...");
                        Console.WriteLine("");
                        #region Upload the file contents to virtual drive
                        API.Initialize( new Guid(VirtualDriveSpec.VirtualDiskSessionToken.Substring(1)),
                                        new Guid(VirtualDriveSpec.VolumeSessionToken.Substring(1)),
                                        userSessionInfo.UserSessionToken);
                        using (var wcfs = new Unosys.SDK.FileStream(wcContentFileName, Unosys.SDK.FileMode.Create, Unosys.SDK.FileAccess.ReadWrite,
                                                Unosys.SDK.FileShare.None))
                        {
                            fs.CopyTo(wcfs);
                            wcfs.Flush();
                            wcfs.Close();
                        }
                        #endregion
                     }
                    #endregion

                    // var result = CallGeneralLedgerDefundWalletApiOnNode(userSessionInfo.UserSessionToken, dltAddress, dltPrivateKey, fundsAmount);
                    Console.WriteLine("");
                    Console.WriteLine("");
                    Console.WriteLine($"Media Content Title: '{contentTitle}' successfully published to the Virtual Drive");
                }
                catch (Exception)
                {

                    throw;
                }
                finally
                {
                    Console.ForegroundColor = ogrForegroundColor;
                }
                #endregion 


               
            }
            #endregion

            //#region Step #7 - Display result
            //Program.DisplaySuccessfulWriteLine($"");
            ////Program.DisplaySuccessfulWriteLine($"REPORTS for {userSessionInfo.UserName}: ");
            ////Program.DisplaySuccessfulWriteLine("==================================================================================");
            ////Program.DisplaySuccessfulWriteLine($"");
            //Program.DisplaySuccessfulWriteLine(result);
            //#endregion

        }
        

        private void AccessContent()
        {
            UserSessionInfo userSessionInfo = null!;

            #region  Step #1:  Check if a simulation has already been created and if noto error
            if (!Simulator.NetworkAlreadyCreated())
            {
                throw new CommandLineToolInvalidOperationException($"A World Computer node network does not exist. Use 'WCSim NODE /Create' to create one first.");
            }
            #endregion

            #region Step #2: Deserialize the SIMULATOR_SETTINGS_FILE_NAME settings file
            NetworkSpec = Simulator.GetNetworkSpec();//)JsonSerializer.Deserialize<NetworkSpec>(fs);
            //using (System.IO.FileStream fs = new System.IO.FileStream(System.IO.Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_SETTINGS_FILE_NAME), 
            //    System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
            //{
            //    NetworkSpec = (NetworkSpec)JsonSerializer.Deserialize<NetworkSpec>(fs);
            //}
            #endregion

            #region Step #3: Check if a node network is not already running and if so error
            if (!NetworkCommandContext.NetworkAlreadyRunning(NetworkSpec))
            {
                throw new CommandLineToolInvalidOperationException("A World Computer node network is not already running.  Use NODE /START to start one running one first.");
            }
            #endregion

            #region  Step #4:  Check if a vdrive has already been created and if so error
            if (!Simulator.VirtualDriveAlreadyCreated())
            {
                throw new CommandLineToolInvalidOperationException($"A World Computer Virtual Drive does not exist. Use 'VDRIVE /Create' to create a one first.");
            }
            #endregion

            #region Step #5: Deserialize the SIMULATOR_VDRIVE_FILE_NAME settings file
            VirtualDriveSpec = Simulator.GetVirtualDriveSpec();
            #endregion

            #region Step #4:  Check if user already logged in
            var userLoggedIn = Simulator.UserAlreadyLoggedIn(clientNodeNumber, userName);
            if (!userLoggedIn)
            {
                if (!string.IsNullOrEmpty(userName))
                {
                    throw new CommandLineToolInvalidOperationException($"Cannot perform operation because User {userName.ToUpper()} is not logged in.");
                }
                else
                {
                    throw new CommandLineToolInvalidOperationException($"Cannot perform operation because no User context is established - use /USER switch, or login as a User.");
                }
            }
            else
            {
                if (userLoggedIn)
                {
                    #region Step #5: Deserialize UserSessionInfo to obtain the Token of the Logged in User session
                    userSessionInfo = Simulator.GetUserSessionInfo(clientNodeNumber, userName);
                    if (userSessionInfo == null)
                    {
                        throw new CommandLineToolInvalidOperationException($"Corrupted WCSim user file.");
                    }
                    userName = userSessionInfo.UserName;
                    #endregion
                }

                #region Step #6 - Fetch list of published Content to Present to User
                var vDiskRootFolderName = $"Node{clientNodeNumber}_Root_{new Guid(VirtualDriveSpec.VirtualDiskSessionToken.Substring(1)).ToString("N").ToUpper()}";
                var path = System.IO.Path.Combine(@".\LocalStore", vDiskRootFolderName);
                var contentFiles = System.IO.Directory.GetFiles(path, "*.cnt");
                #endregion 

                #region Step #6 - Prompt User for Content to Access 
                var ogrForegroundColor = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("");
                    Console.WriteLine("");
                    Console.WriteLine("Enter the # of the Media file to access: ");
                    decimal decPrice = 0.00m;
                    string[] ContentDIDRefs = new string[contentFiles.Length];
                    string[] ContentFileNames = new string[contentFiles.Length];
                    string[] Prices = new string[contentFiles.Length];
                    string[] Titles = new string[contentFiles.Length];
                    string[] UserNames = new string[contentFiles.Length];
                    string contentIndex = null!;
                    bool noSelectionMade = false;
                    while (true)
                    {
                        for (int index = 0; index < contentFiles.Length; index++)
                        {
                            var absoluteFileName = System.IO.Path.GetFileName(contentFiles[index]);
                            ContentFileNames[index] = ConvertBytesToHexString( Encoding.Unicode.GetBytes(absoluteFileName) );
                            var x = Encoding.Unicode.GetString(ConvertHexStringToBytes(ContentFileNames[index]));
                            var contentFileName = System.IO.Path.GetFileName(contentFiles[index]);
                            ContentDIDRefs[index] = contentFileName.Substring(0, 32);
                            Prices[index] = contentFileName.Substring(32, 16).Replace("_",".");
                            var pathWithoutCntExtension = System.IO.Path.GetFileNameWithoutExtension(contentFileName);
                            UserNames[index] = System.IO.Path.GetExtension(pathWithoutCntExtension).Substring(1);
                            var contentFileProper = System.IO.Path.GetFileNameWithoutExtension(pathWithoutCntExtension);
                            //Titles[index] = contentFileName.Substring(49);
                            Titles[index] = contentFileProper.Substring(49);
                            decPrice = decimal.Parse(Prices[index]);
                            Console.WriteLine($"{(index+1).ToString().PadLeft(3, ' ')}. {(decPrice == 0.00m ? "free" : "$ " + decPrice.ToString("F2"))}  {"\t\t"+Titles[index]}");
                        }
                        Console.WriteLine($"{"Q".PadLeft(3, ' ')}. Quit");
                        Console.ForegroundColor = ConsoleColor.White;
                        contentIndex = Console.ReadLine();


                        if (contentIndex.ToUpper() == "Q")
                        {
                            noSelectionMade = true;
                            break;
                        }
                        else
                        {
                            int index = 0;
                            if( !int.TryParse(contentIndex, out index) || index < 1 || index > contentFiles.Length)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("");
                                Console.WriteLine("Invalid selection....please try again.");
                                Console.WriteLine("");
                                Console.WriteLine("");
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine("Enter the # of the Media file to access: ");
                                continue;
                            }

                            decPrice = decimal.Parse(Prices[index-1]);
                            Console.WriteLine(decPrice.ToString());
                            if (decPrice > 0.00m && !(UserNames[index-1].ToUpper().Equals(userName.ToUpper())))
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine($"Your media selection costs ${decPrice.ToString("F2")} to access.  Would you like to access it (Y/N)?");
                                Console.ForegroundColor = ConsoleColor.White;
                                var answer = Console.ReadKey();
                                string result = null!;
                                if (answer.KeyChar == 'Y' || answer.KeyChar == 'y')
                                {
                                    Console.WriteLine("");
                                    Console.WriteLine("");
                                    Console.WriteLine("Launching your selected Media - One Moment Please...");
                                    #region Call GeneralLedgerPurchaseContent on API
                                    result = CallGeneralLedgerPurchaseContentOnAPI(userSessionInfo.UserSessionToken, ContentDIDRefs[index - 1], decPrice);
                                    if (result == null)
                                    {
                                        break;
                                    }
                                    Console.WriteLine("");
                                    Console.WriteLine("");
                                    Console.WriteLine(result);
                                    #endregion

                                    PlayContent(userSessionInfo, ContentFileNames, index); 
                                    break;
                                }
                                else
                                {
                                    Console.WriteLine("");
                                    Console.WriteLine("");
                                    Console.ForegroundColor = ConsoleColor.Cyan;
                                    Console.WriteLine("Enter the # of the Media file to access: ");
                                    continue;
                                }
                            }
                            else
                            {
                                Console.WriteLine("");
                                Console.WriteLine("");
                                Console.WriteLine($"Launching your selected Media... enjoy! ");
                                PlayContent(userSessionInfo, ContentFileNames, index);
                                break;
                            }
                        }
                    }

                    #region Step #8 - Call CallGeneralLedgerDefundWalletApiOnNode
                    // var result = CallGeneralLedgerDefundWalletApiOnNode(userSessionInfo.UserSessionToken, dltAddress, dltPrivateKey, fundsAmount);
                    Console.WriteLine("");
                    Console.WriteLine("");
                    if(noSelectionMade)
                        Console.WriteLine($"No Media content selection was made.");
                    else
                        Console.WriteLine($"");
                    #endregion
                }
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    Console.ForegroundColor = ogrForegroundColor;
                }
                #endregion
            }
            #endregion

            //#region Step #7 - Display result
            //Program.DisplaySuccessfulWriteLine($"");
            ////Program.DisplaySuccessfulWriteLine($"REPORTS for {userSessionInfo.UserName}: ");
            ////Program.DisplaySuccessfulWriteLine("==================================================================================");
            ////Program.DisplaySuccessfulWriteLine($"");
            //Program.DisplaySuccessfulWriteLine(result);
            //#endregion

        }

        private void PlayContent(UserSessionInfo userSessionInfo, string[] ContentFileNames, int index)
        {
            #region Play content for user
            var contentRef = $"{Program.MediaPlayerServerPort} " +
                $"{new Guid(VirtualDriveSpec.VirtualDiskSessionToken.Substring(1)).ToString("N").ToUpper()}{new Guid(VirtualDriveSpec.VolumeSessionToken.Substring(1)).ToString("N").ToUpper()}{userSessionInfo.UserSessionToken.Substring(1)}{ContentFileNames[index - 1]}";
            try
            {
                if (!Simulator.MediaServerAlreadyRunning())
                {
                    Process proc = new Process();
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.WorkingDirectory = Program.NodeDirectory;
                    psi.FileName = System.IO.Path.Combine(Program.NodeDirectory, Program.MediaPlayerAppName);
                    psi.UseShellExecute = true;
                    psi.WindowStyle = ProcessWindowStyle.Minimized;
                    psi.Arguments = $"{contentRef}";
                    proc.StartInfo = psi;
                    proc.Start();
                    Task.Delay(5).Wait();
                }

                try
                {
                    Process proc2 = new Process();
                    ProcessStartInfo psi2 = new ProcessStartInfo();
                    psi2.WorkingDirectory = Program.NodeDirectory;
                    psi2.FileName = Program.WebBrowserPath;
                    psi2.UseShellExecute = true;
                    psi2.WindowStyle = ProcessWindowStyle.Normal;
                    psi2.Arguments = $"--new-window https://localhost:{Program.MediaPlayerServerPort}/api/Media?contentRef={ContentFileNames[index - 1]}";
                    proc2.StartInfo = psi2;
                    proc2.Start();
                    Task.Delay(5).Wait();
                }
                catch (Exception)
                {
                    Console.WriteLine($"*** Failed to launch web browser - {Program.WebBrowserPath} - check configuration in appSettings.json.");
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"*** Error attempting to start Media player - {ex.Message}  ");
            }
            #endregion
        }

        private string CallGeneralLedgerPurchaseContentOnAPI( string buyerSessionToken, string contentDIDRef, decimal price)
        {
            string result = null!;
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.GeneralLedgerPurchaseContent*/755},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{Program.WC_OSUSER_SESSION_TOKEN}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""BuyerSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{buyerSessionToken}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""ContentDIDRef"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{contentDIDRef}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""Price"",
                                        ""T"" : ""System.Decimal"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{price}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var nodeName = $"{Program.NodeExecutableName} #{clientNodeNumber}";
            try
            {
                var response = Program.UnoSysApiConnection.PostAsync(content).Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Retrieve result of call
                    //result = $"{amount.ToString("N0")} TBar successfully deposited to GL of {userName.ToUpper()} from Crypto Account: {dltAddress} - Journal Entry Posted:" + Environment.NewLine + Environment.NewLine;
                    result = "";
                    var responseJson = response.Content.ReadAsStringAsync().Result;
                    using var doc = JsonDocument.Parse(responseJson);
                    var element = doc.RootElement;
                    result += element.GetProperty("O")[0].GetProperty("V").GetString()!;
                }
                else
                {
                    string reason = null!;
                    try
                    {
                        reason = Encoding.UTF8.GetString(response.Content.ReadAsByteArrayAsync().Result);
                    }
                    catch (Exception)
                    {
                        reason = "No reason given.";
                        throw;
                    }
                    Program.DisplaySuccessfulWriteLine($"Error calling GeneralLedgerPurchaseContent() - response.StatusCode={response.StatusCode}{(reason != null ? $" - ({reason})" : "")}");
                    result = null!;
                }
            }
            catch (Exception ex)
            {
                Program.DisplayUnsuccessfulWriteLine($"Error in GeneralLedgerPurchaseContent(): {nodeName} - {ex} ");
            }
            return result;

        }

        internal static string WCVirtualDiskVolumeDataOperationApi(string base64Operation)
        {
            string result = null!;
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.VirtualDiskVolumeDataOperation*/ 725},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{Program.WC_OSUSER_SESSION_TOKEN}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""Operation"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{base64Operation}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = Program.UnoSysApiConnection.PostAsync(content).Result;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                // Retrieve result of call
                var responseJson = response.Content.ReadAsStringAsync().Result;
                using var doc = JsonDocument.Parse(responseJson);
                var element = doc.RootElement;
                result = element.GetProperty("O")[0].GetProperty("V").GetString()!;
            }
            else
            {
                Program.DisplayUnsuccessfulWriteLine($"Error calling WCVirtualDiskVolumeDataOperationApi() - response.StatusCode={response.StatusCode}");
            }
            return result;
        }


        internal static string WCVirtualDiskVolumeMetaDataOperationApi(string base64Operation)
        {
            string result = null!;
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.VirtualDiskVolumeMetaDataOperation*/ 724},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{Program.WC_OSUSER_SESSION_TOKEN}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""Operation"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{base64Operation}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = Program.UnoSysApiConnection.PostAsync(content).Result;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                // Retrieve result of call
                var responseJson = response.Content.ReadAsStringAsync().Result;
                using var doc = JsonDocument.Parse(responseJson);
                var element = doc.RootElement;
                result = element.GetProperty("O")[0].GetProperty("V").GetString()!;
            }
            else
            {
                Program.DisplayUnsuccessfulWriteLine($"Error calling WCVirtualDiskVolumeMetaDataOperationApi() - response.StatusCode={response.StatusCode}");
            }
            return result;
        }


        private static string ConvertBytesToHexString(byte[] buffer)
        {
            byte[] bytes = new byte[buffer.Length];
            Buffer.BlockCopy(buffer, 0, bytes, 0, buffer.Length);
            string sbytes = BitConverter.ToString(bytes);       // Convert to hyphen delimited string of hex characters
            return sbytes.Replace("-", "");
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


        static public class Console2
        {
            /// <summary>
            /// Like System.Console.ReadLine(), only with a mask.
            /// </summary>
            /// <param name="mask">a <c>char</c> representing your choice of console mask</param>
            /// <returns>the string the user typed in </returns>
            public static string ReadPassword(char mask)
            {
                const int ENTER = 13, BACKSP = 8, CTRLBACKSP = 127;
                int[] FILTERED = { 0, 27, 9, 10 /*, 32 space, if you care */ }; // const

                var pass = new Stack<char>();
                char chr = (char)0;

                while ((chr = System.Console.ReadKey(true).KeyChar) != ENTER)
                {
                    if (chr == BACKSP)
                    {
                        if (pass.Count > 0)
                        {
                            System.Console.Write("\b \b");
                            pass.Pop();
                        }
                    }
                    else if (chr == CTRLBACKSP)
                    {
                        while (pass.Count > 0)
                        {
                            System.Console.Write("\b \b");
                            pass.Pop();
                        }
                    }
                    else if (FILTERED.Count(x => chr == x) > 0) { }
                    else
                    {
                        pass.Push((char)chr);
                        System.Console.Write(mask);
                    }
                }

                System.Console.WriteLine();

                return new string(pass.Reverse().ToArray());
            }

            /// <summary>
            /// Like System.Console.ReadLine(), only with a mask.
            /// </summary>
            /// <returns>the string the user typed in </returns>
            public static string ReadPassword()
            {
                return Console2.ReadPassword('*');
            }
        }
        
        private string CallGeneralLedgerGetReportApiOnNode(string userSessionToken, int reportType, int reportOutputType, string fromUtcDate, string toUtcDate )
        {
            string result = null!;
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.GeneralLedgerGetReport*/750},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{Program.WC_OSUSER_SESSION_TOKEN}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""SubjectSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : true
                                    }},
                            ""V""  : ""{userSessionToken}"",
                            ""N""  : true
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""ReportType"",
                                        ""T"" : ""System.Int32"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{reportType}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""ReportOutputType"",
                                        ""T"" : ""System.Int32"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{reportOutputType}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""FromUtcDate"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{fromUtcDate}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""ToUtcDate"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{toUtcDate}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var nodeName = $"{Program.NodeExecutableName} #{clientNodeNumber}";
            try
            {

                var response = Program.UnoSysApiConnection.PostAsync(content).Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Retrieve result of call
                    var responseJson = response.Content.ReadAsStringAsync().Result;
                    using var doc = JsonDocument.Parse(responseJson);
                    var element = doc.RootElement;
                    result = element.GetProperty("O")[0].GetProperty("V").GetString()!;
                }
                else
                {
                    Program.DisplaySuccessfulWriteLine($"Error calling GeneralLedgerGetReport() - response.StatusCode={response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Program.DisplayUnsuccessfulWriteLine($"Error in CallGeneralLedgerGetReportApiOnNode(): {nodeName} - {ex} ");
            }
            return result;

        }


        private string CallGeneralLedgerFundWalletApiOnNode(string userSessionToken, string dltAddress, string dltPrivateKey, ulong amount)
        {
            string result = null!;
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.GeneralLedgerFundWallet*/751},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{Program.WC_OSUSER_SESSION_TOKEN}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""SubjectSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : true
                                    }},
                            ""V""  : ""{userSessionToken}"",
                            ""N""  : true
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""DLTAddress"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{dltAddress}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""DLTPrivateKey"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{dltPrivateKey}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""FundsAmount"",
                                        ""T"" : ""System.UInt64"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{amount}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var nodeName = $"{Program.NodeExecutableName} #{clientNodeNumber}";
            try
            {
                var response = Program.UnoSysApiConnection.PostAsync(content).Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Retrieve result of call
                    result = $"{amount.ToString("N0")} TBar successfully deposited to GL of {userName.ToUpper()} from Crypto Account: {dltAddress} - Journal Entry Posted:" + Environment.NewLine + Environment.NewLine;
                    var responseJson = response.Content.ReadAsStringAsync().Result;
                    using var doc = JsonDocument.Parse(responseJson);
                    var element = doc.RootElement;
                    result += element.GetProperty("O")[0].GetProperty("V").GetString()!;
                }
                else
                {
                    string reason = null!;
                    try
                    {
                        reason = Encoding.UTF8.GetString(response.Content.ReadAsByteArrayAsync().Result);
                    }
                    catch (Exception)
                    {
                        reason = "No reason given.";
                        throw;
                    }
                    Program.DisplaySuccessfulWriteLine($"Error calling GeneralLedgerFundWallet() - response.StatusCode={response.StatusCode}{(reason != null? $" - ({reason})" : "" )}");
                }
            }
            catch (Exception ex)
            {
                Program.DisplayUnsuccessfulWriteLine($"Error in CallGeneralLedgerFundWalletApiOnNode(): {nodeName} - {ex} ");
            }
            return result;

        }

        private string CallGeneralLedgerDefundWalletApiOnNode(string userSessionToken, string dltAddress, string dltPrivateKey, ulong amount)
        {
            string result = null!;
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.GeneralLedgerFundWallet*/752},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{Program.WC_OSUSER_SESSION_TOKEN}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""SubjectSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : true
                                    }},
                            ""V""  : ""{userSessionToken}"",
                            ""N""  : true
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""DLTAddress"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{dltAddress}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""DLTPrivateKey"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{dltPrivateKey}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""FundsAmount"",
                                        ""T"" : ""System.UInt64"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{amount}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var nodeName = $"{Program.NodeExecutableName} #{clientNodeNumber}";
            try
            {

                var response = Program.UnoSysApiConnection.PostAsync(content).Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    result = $"{(amount == ulong.MaxValue ? "All" : amount.ToString("N0"))} TBar successfully withdrawn from GL of {userName.ToUpper()} to Crypto Account: {dltAddress} - Journal Entry Posted:" + Environment.NewLine + Environment.NewLine;
                    var responseJson = response.Content.ReadAsStringAsync().Result;
                    using var doc = JsonDocument.Parse(responseJson);
                    var element = doc.RootElement;
                    result += element.GetProperty("O")[0].GetProperty("V").GetString()!;
                }
                else
                {
                    string reason = null!;
                    try
                    {
                        reason = Encoding.UTF8.GetString(response.Content.ReadAsByteArrayAsync().Result);
                    }
                    catch (Exception)
                    {
                        reason = "No reason given.";
                        throw;
                    }
                    Program.DisplaySuccessfulWriteLine($"Error calling GeneralLedgerDefundWallet() - response.StatusCode={response.StatusCode}{(reason != null ? $" - ({reason})" : "")}");
                }
            }
            catch (Exception ex)
            {
                Program.DisplayUnsuccessfulWriteLine($"Error in CallGeneralLedgerDefundWalletApiOnNode(): {nodeName} - {ex} ");
            }
            return result;

        }

        #endregion
    }

   
}
