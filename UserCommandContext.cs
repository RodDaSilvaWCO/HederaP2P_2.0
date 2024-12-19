using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Text.Json.Serialization;

namespace WorldComputer.Simulator
{

    public class UserCommandContext : ICommandContext
    {
        #region Field Members
        const int MAX_CLUSTER_COUNT = 63;
        const int MAX_NODES = 100;
        const int DEFAULT_CLIENT_NODE_NUMBER = 1;


        Hashtable switchSet = new Hashtable(20);
        string[] cmdArgs;
        int clusterSize = 1;
        string userName = null!;
        bool createUser = false;
        bool deleteUser = false;
        bool clientSimulator = false;
        bool loginUser = false;
        bool logoutUser = false;
        bool simulatorHelp = false;
        int clientNodeNumber = DEFAULT_CLIENT_NODE_NUMBER;
        NetworkSpec NetworkSpec = null;
        VDriveInfo VirtualDriveSpec = null!;

        #endregion

        #region Constructors
        public UserCommandContext(string[] commandContextArgs)
        {
            cmdArgs = commandContextArgs;
            // Define valid context switches
            string[] allowedSwitches = new string[6];
            allowedSwitches[0] = "/CREATE:";
            allowedSwitches[1] = "/LOGIN";
            allowedSwitches[2] = "/LOGOUT";
            allowedSwitches[3] = "/DELETE";
            allowedSwitches[4] = "/CLIENTNODE:";
            allowedSwitches[5] = "/?";
            CommandLineTool.ParseCommandContextSwitches(commandContextArgs, allowedSwitches, switchSet, this);
            simulatorHelp = (bool)switchSet["/?"];
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
                Program.UnoSysApiConnection = new UnoSysConnection(string.Format(Program.UnoSysApiUrlTemplate, Program.BasePort + ((clientNodeNumber - 1) * Program.BasePortSpacing)));

                #region Create User
                if (createUser)
                {
                    CreateUser();

                }
                #endregion

                #region Login User
                if (loginUser)
                {
                    LoginUser();
                }
                #endregion

                #region Logout User
                if (logoutUser)
                {
                    LogoutUser();
                }
                #endregion

                #region Delete User
                if (deleteUser)
                {
                    DeleteUser();
                }
                #endregion
            }
        }

        public bool ValidateContext()
        {
            bool invalidSwitch = false;
            object switchValue = null;

            #region Switch Processing
            #region Create switch processing
            //Examples of possible legal /Create switches:  / Create:Alice@Alice.com
            switchValue = switchSet["/CREATE"];
            if (switchValue is string)
            {
                if (!string.IsNullOrEmpty((string)switchValue))
                {
                    string[] values = ((string)switchValue).Split(new char[] { ',' }, StringSplitOptions.None); // NOTE: We do NOT throw away empty values in order to check for invalid switches
                    switch (values.Length)
                    {
                        case 0:
                            throw new CommandLineToolInvalidSwitchArgumentException("/CREATE: - missing UserName argument.");

                        case 1:
                            if (string.IsNullOrEmpty(values[0]))
                            {
                                invalidSwitch = true;
                            }
                            else
                            {
                                userName = values[0];
                                createUser = true;
                            }
                            break;

                        default:
                            invalidSwitch = true;
                            break;
                    }
                    if (invalidSwitch)
                    {
                        throw new CommandLineToolInvalidSwitchException("/CREATE:" + switchValue);
                    }
                }
            }
            else
            {
                if ((bool)switchValue)
                {
                    throw new CommandLineToolInvalidSwitchArgumentException("/CREATE: - missing UserName argument.");
                }

            }
            #endregion

            #region Delete switch processing
            //Examples of possible legal /Delete switches:  / Delete:Alice@Alice.com
            switchValue = switchSet["/DELETE"];
            if (switchValue is string)
            {
                if (!string.IsNullOrEmpty((string)switchValue))
                {
                    string[] values = ((string)switchValue).Split(new char[] { ',' }, StringSplitOptions.None); // NOTE: We do NOT throw away empty values in order to check for invalid switches
                    switch (values.Length)
                    {
                        case 0:
                            throw new CommandLineToolInvalidSwitchArgumentException("/DELETE: - missing UserName argument.");

                        case 1:
                            if (string.IsNullOrEmpty(values[0]))
                            {
                                invalidSwitch = true;
                            }
                            else
                            {
                                userName = values[0];
                                deleteUser = true;
                            }
                            break;

                        default:
                            invalidSwitch = true;
                            break;
                    }
                    if (invalidSwitch)
                    {
                        throw new CommandLineToolInvalidSwitchException("/DELETE:" + switchValue);
                    }
                }
            }
            else
            {
                if ((bool)switchValue)
                {
                    throw new CommandLineToolInvalidSwitchArgumentException("/DELETE: - missing UserName argument.");
                }

            }
            #endregion

            #region Login switch processing
            //Examples of possible legal /Login switches:  / Login:Alice@Alice.com
            switchValue = switchSet["/LOGIN"];
            if (switchValue is string)
            {
                if (!string.IsNullOrEmpty((string)switchValue))
                {
                    string[] values = ((string)switchValue).Split(new char[] { ',' }, StringSplitOptions.None); // NOTE: We do NOT throw away empty values in order to check for invalid switches
                    switch (values.Length)
                    {
                        case 0:
                            throw new CommandLineToolInvalidSwitchArgumentException("/LOGIN: - missing UserName argument.");

                        case 1:
                            if (string.IsNullOrEmpty(values[0]))
                            {
                                invalidSwitch = true;
                            }
                            else
                            {
                                userName = values[0];
                                loginUser = true;
                            }
                            break;

                        default:
                            invalidSwitch = true;
                            break;
                    }
                    if (invalidSwitch)
                    {
                        throw new CommandLineToolInvalidSwitchException("/LOGIN:" + switchValue);
                    }
                }
            }
            else
            {
                if ((bool)switchValue)
                {
                    throw new CommandLineToolInvalidSwitchArgumentException("/LOGIN: - missing UserName argument.");
                }
            }
            #endregion

            #region Logout switch processing
            //Examples of possible legal /Logout switches: /Logout, /Logout:, /Logout:Alice@Alice.com
            switchValue = switchSet["/LOGOUT"];
            if (switchValue is string)
            {
                if (!string.IsNullOrEmpty((string)switchValue))
                {
                    string[] values = ((string)switchValue).Split(new char[] { ',' }, StringSplitOptions.None); // NOTE: We do NOT throw away empty values in order to check for invalid switches
                    switch (values.Length)
                    {
                        case 0:
                            #region Check if only a single logged in user
                            switch (Simulator.LoggedInUserCount(clientNodeNumber))
                            {
                                case 0:
                                    {
                                        throw new CommandLineToolInvalidSwitchArgumentException("/LOGOUT: no User found logged in to logout.");
                                    }
                                case 1:
                                    {
                                        logoutUser = true;
                                        break;
                                    }
                                default:
                                    {
                                        throw new CommandLineToolInvalidSwitchArgumentException("/LOGOUT: multiple Users logged in, so must specify optional UserName argument.");
                                    }
                            }
                            //if (LoggedInUserCount() != 1)
                            //{
                            //    throw new CommandLineToolInvalidSwitchArgumentException("/LOGOUT: multiple Users logged in, so must specify optional UserName argument.");
                            //}
                            //else
                            //{
                            //    logoutUser = true;
                            //}
                            #endregion 
                            break;

                        case 1:
                            if (string.IsNullOrEmpty(values[0]))
                            {
                                invalidSwitch = true;
                            }
                            else
                            {
                                userName = values[0];
                                logoutUser = true;
                            }
                            break;

                        default:
                            invalidSwitch = true;
                            break;
                    }
                    if (invalidSwitch)
                    {
                        throw new CommandLineToolInvalidSwitchException("/LOGOUT:" + switchValue);
                    }
                }
            }
            else
            {
                if ((bool)switchValue)
                {
                    switch (Simulator.LoggedInUserCount(clientNodeNumber))
                    {
                        case 0:
                            {
                                throw new CommandLineToolInvalidSwitchArgumentException("/LOGOUT: no User found logged in to logout.");
                            }
                        case 1:
                            {
                                logoutUser = true;
                                break;
                            }
                        default:
                            {
                                throw new CommandLineToolInvalidSwitchArgumentException("/LOGOUT: multiple Users logged in, so must specify optional UserName argument.");
                            }
                    }
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
            if (clientSimulator && !(createUser || deleteUser || loginUser || logoutUser))
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/CLIENTNODE can only be used with another switch.");
            }

            if (createUser && deleteUser)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/CREATE and /DELETE cannot be used together.");
            }

            if (createUser && loginUser)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/CREATE and /LOGIN cannot be used together.");
            }

            if (createUser && logoutUser)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/CREATE and /LOGOUT cannot be used together.");
            }

            if (deleteUser && loginUser)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/DELETE and /LOGIN cannot be used together.");
            }

            if (deleteUser && logoutUser)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/DELETE and /LOGOUT cannot be used together.");
            }

            if (loginUser && logoutUser)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/LOGIN and /LOGOUT cannot be used together.");
            }

            #endregion

            bool result = (createUser || loginUser || logoutUser || deleteUser || clientSimulator || simulatorHelp);
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
            Program.DisplaySuccessfulWriteLine("WorldComputer USER command usage:");
            Program.DisplaySuccessfulWriteLine("");
            Program.DisplaySuccessfulWriteLine("      WCSim USER | U  [<switches>]");
            Program.DisplaySuccessfulWriteLine("");
            Program.DisplaySuccessfulWriteLine("where <switches> are one or more of:");
            Program.DisplaySuccessfulWriteLine("");
            Program.DisplaySuccessfulWriteLine( " /CREATE:UserName\t\tCreate a unique WorldComputer User");
            Program.DisplaySuccessfulWriteLine( " /LOGIN:UserName\t\tLogin User");
            Program.DisplaySuccessfulWriteLine( " /LOGOUT[:UserName]\t\tLogout User (currently logged in User)");
            Program.DisplaySuccessfulWriteLine( " /DELETE:UserName\t\tDelete a WorldComputer User");
            Program.DisplaySuccessfulWriteLine($" /CLIENTNODE:<Node#>\t\tClient Node # to connect to - ({DEFAULT_CLIENT_NODE_NUMBER})");
            Program.DisplaySuccessfulWriteLine( " /?\t\t\t\tUsage information");
            Program.DisplaySuccessfulWriteLine("");
            Program.DisplaySuccessfulWriteLine("=======================================================================================================");
        }
        #endregion


        #region Helpers
        private bool NetworkSimulationAlreadyCreated()
        {
            return File.Exists(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_SETTINGS_FILE_NAME));
        }


        private void CreateUser()
        {
            #region  Step #1:  Check if a simulation has already been created and if noto error
            if (!Simulator.NetworkAlreadyCreated())
            {
                throw new CommandLineToolInvalidOperationException($"A World Computer node network does not exist. Use 'WCSim NODE /Create' to create one first.");
            }
            #endregion

            #region Step #2: Deserialize the SIMULATOR_SETTINGS_FILE_NAME settings file
            using (FileStream fs = new FileStream(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_SETTINGS_FILE_NAME), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                NetworkSpec = (NetworkSpec)JsonSerializer.Deserialize<NetworkSpec>(fs);
            }
            #endregion

            #region Step #3: Check if a node network is not already running and if so error
            if (!NetworkCommandContext.NetworkAlreadyRunning(NetworkSpec))
            {
                throw new CommandLineToolInvalidOperationException("A World Computer node network is not already running.  Use NODE /START to start one running one first.");
            }
            #endregion

            #region  Step #4:  Call the WorldComputer UserCreate() Api to create the user
            var result = CallUserCreateApiOnNode(userName);
            #endregion


            #region Step #5 - Display result
            if (result == HttpStatusCode.OK)
            {
                Program.DisplaySuccessfulWriteLine($"");
                Program.DisplaySuccessfulWriteLine($"User {userName} was successfully created.");
            }
            else
            {
                Program.DisplayUnsuccessfulWriteLine($"");
                Program.DisplayUnsuccessfulWriteLine($"User {userName} was NOT successfully created - reason: {result}.");
            }
            #endregion

        }

        private void LoginUser()
        {
            #region  Step #1:  Check if a simulation has already been created and if noto error
            if (!Simulator.NetworkAlreadyCreated())
            {
                throw new CommandLineToolInvalidOperationException($"A World Computer node network does not exist. Use 'WCSim NODE /Create' to create one first.");
            }
            #endregion

            #region Step #2: Deserialize the SIMULATOR_SETTINGS_FILE_NAME settings file
            using (FileStream fs = new FileStream(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_SETTINGS_FILE_NAME), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                NetworkSpec = (NetworkSpec)JsonSerializer.Deserialize<NetworkSpec>(fs);
            }
            #endregion

            #region Step #3: Check if a node network is not already running and if so error
            if (!NetworkCommandContext.NetworkAlreadyRunning(NetworkSpec))
            {
                throw new CommandLineToolInvalidOperationException("A World Computer node network is not already running.  Use NODE /START to start one running one first.");
            }
            #endregion

            (HttpStatusCode statusCode, string userSessionToken) result = (HttpStatusCode.OK, null!);
            #region Step #4:  Check if user already logged in
            if (Simulator.UserAlreadyLoggedIn(clientNodeNumber, userName))
            {
                throw new CommandLineToolInvalidOperationException($"Cannot login UserName {userName} because the user is ALREADY logged in.");
            }
            else
            {
                #region  Step #5:  Call the WorldComputer UserLogin() Api to login the user
                result = CallUserLoginApiOnNode(userName);
                #endregion
            }
            #endregion

            #region Step #6 - Display result
            if (result.statusCode == HttpStatusCode.OK)
            {
                Program.DisplaySuccessfulWriteLine($"");
                Program.DisplaySuccessfulWriteLine($"User {userName} was successfully logged in.");

                #region  Step #7: Persist the UserSessionInfo object to disk so it can be automatically used in other sessions
                //using (var currentUserStream = new FileStream(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_CURRENT_USERS_FILE_NAME + "_" + userName + ".json"), FileMode.Create, FileAccess.Write, FileShare.None))
                //{
                //    var userSessionInfo = new UserSessionInfo(userName, result.userSessionToken);
                //    JsonSerializer.Serialize<UserSessionInfo>(currentUserStream, userSessionInfo);
                //    currentUserStream.Flush();
                //}
                Simulator.PersistUserSessionToken(clientNodeNumber, userName, result.userSessionToken);
                #endregion
            }
            else
            {
                Program.DisplayUnsuccessfulWriteLine($"");
                Program.DisplayUnsuccessfulWriteLine($"User {userName} was NOT successfully logged in - reason: {result.statusCode}.");
            }
            #endregion

        }


        private void LogoutUser()
        {
            #region  Step #1:  Check if a simulation has already been created and if noto error
            if (!Simulator.NetworkAlreadyCreated())
            {
                throw new CommandLineToolInvalidOperationException($"A World Computer node network does not exist. Use 'WCSim NODE /Create' to create one first.");
            }
            #endregion

            #region Step #2: Deserialize the SIMULATOR_SETTINGS_FILE_NAME settings file
            using (FileStream fs = new FileStream(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_SETTINGS_FILE_NAME), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                NetworkSpec = (NetworkSpec)JsonSerializer.Deserialize<NetworkSpec>(fs);
            }
            #endregion

            #region Step #3: Check if a node network is not already running and if so error
            if (!NetworkCommandContext.NetworkAlreadyRunning(NetworkSpec))
            {
                throw new CommandLineToolInvalidOperationException("A World Computer node network is not already running.  Use NODE /START to start one running one first.");
            }
            #endregion

            HttpStatusCode result = HttpStatusCode.OK;
            #region Step #4:  Check if user already logged in
            if (!Simulator.UserAlreadyLoggedIn(clientNodeNumber, userName))
            {
                throw new CommandLineToolInvalidOperationException($"Cannot logout UserName {userName} because the user is NOT logged in.");
            }
            else
            {
                #region Step #5: Deserialize UserSessionInfo to obtain the Token of the Logged in User session
                UserSessionInfo userSessionInfo = Simulator.GetUserSessionInfo(clientNodeNumber, userName);
                if (userSessionInfo == null ) // || !userName.Equals(userSessionInfo.UserName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CommandLineToolInvalidOperationException($"Corrupted WCSim user file.");
                }
                userName = userSessionInfo.UserName;
                #endregion

                #region  Step #6:  Call the WorldComputer UserLogout() Api to logout the user
                result = CallUserLogoutApiOnNode(userSessionInfo.UserSessionToken);
                #endregion
            }
            #endregion

            #region Step #7 - Display result
            if (result == HttpStatusCode.OK)
            {
                Program.DisplaySuccessfulWriteLine($"");
                Program.DisplaySuccessfulWriteLine($"User {userName} was successfully logged out.");

                #region  Step #8: Delete User login sessionfile
                Simulator.DeleteUserSessionInfo(clientNodeNumber, userName);
                #endregion
            }
            else
            {
                Program.DisplayUnsuccessfulWriteLine($"");
                Program.DisplayUnsuccessfulWriteLine($"User {userName} was NOT successfully logged out - reason: {result}.");
            }
            #endregion

        }


        private void DeleteUser()
        {
            #region  Step #1:  Check if a simulation has already been created and if noto error
            if (!Simulator.NetworkAlreadyCreated())
            {
                throw new CommandLineToolInvalidOperationException($"A World Computer node network does not exist. Use 'WCSim NODE /Create' to create one first.");
            }
            #endregion

            #region Step #2: Deserialize the SIMULATOR_SETTINGS_FILE_NAME settings file
            using (FileStream fs = new FileStream(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_SETTINGS_FILE_NAME), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                NetworkSpec = (NetworkSpec)JsonSerializer.Deserialize<NetworkSpec>(fs);
            }
            #endregion

            #region Step #3: Check if a node network is not already running and if so error
            if (!NetworkCommandContext.NetworkAlreadyRunning(NetworkSpec))
            {
                throw new CommandLineToolInvalidOperationException("A World Computer node network is not already running.  Use NODE /START to start one running one first.");
            }
            #endregion

            HttpStatusCode result = HttpStatusCode.OK;
            #region Step #4:  Check if user already logged in
            if (Simulator.UserAlreadyLoggedIn(clientNodeNumber, userName))
            {
                throw new CommandLineToolInvalidOperationException($"Cannot delete UserName {userName} because the user is logged in. Use /LOGOUT to logout the user first.");
            }
            else
            {
                #region  Step #5:  Call the WorldComputer UserLogin() Api to login the user
                result = CallUserDeleteApiOnNode(userName);
                #endregion
            }
            #endregion

            #region Step #6 - Display result
            if (result == HttpStatusCode.OK)
            {
                Program.DisplaySuccessfulWriteLine($"User {userName} was successfully deleted.");
            }
            else
            {
                Program.DisplayUnsuccessfulWriteLine($"User {userName} was NOT successfully deleted - reason: {result}.");
            }
            #endregion
        }

        private HttpStatusCode CallUserCreateApiOnNode(string userName)
        {
            HttpStatusCode result = HttpStatusCode.OK;
            const string WC_OSUSER_SESSION_TOKEN = "U00000000000000000000000000000000";
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.UserCreate*/508},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{WC_OSUSER_SESSION_TOKEN}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""UserName"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{userName}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""Password"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{userName.ToUpper()}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var nodeName = $"{Program.NodeExecutableName} #{clientNodeNumber}";
            try
            {

                var response = Program.UnoSysApiConnection.PostAsync(content).Result;
                result = response.StatusCode;
            }
            catch (Exception ex)
            {
                Program.DisplayUnsuccessfulWriteLine($"Error in CallUserCreateApiOnNode(): {nodeName} - {ex} ");
            }
            return result;

        }


        private (HttpStatusCode, string) CallUserLoginApiOnNode(string userName)
        {
            string userSessionToken = null!;
            HttpStatusCode result = HttpStatusCode.OK;
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.UserLogin*/500},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserName"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{userName}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""Password"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{userName.ToUpper()}"",
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
                    userSessionToken = element.GetProperty("O")[0].GetProperty("V").GetString()!;
                }
                result = response.StatusCode;
            }
            catch (Exception ex)
            {
                Program.DisplayUnsuccessfulWriteLine($"Error in CallUserLoginApiOnNode(): {nodeName} - {ex} ");
            }
            return (result, userSessionToken);

        }


        private HttpStatusCode CallUserLogoutApiOnNode(string userSessionToken)
        {
            HttpStatusCode result = HttpStatusCode.OK;
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.UserLogout*/501},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""SubjectUserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{userSessionToken}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var nodeName = $"{Program.NodeExecutableName} #{clientNodeNumber}";
            try
            {

                var response = Program.UnoSysApiConnection.PostAsync(content).Result;
                result = response.StatusCode;
            }
            catch (Exception ex)
            {
                Program.DisplayUnsuccessfulWriteLine($"Error in CallUserLogoutApiOnNode(): {nodeName} - {ex} ");
            }
            return result;

        }


        private HttpStatusCode CallUserDeleteApiOnNode(string userName)
        {
            HttpStatusCode result = HttpStatusCode.OK;
            const string WC_OSUSER_SESSION_TOKEN = "U00000000000000000000000000000000";
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.UserDelete*/509},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{WC_OSUSER_SESSION_TOKEN}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""UserName"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{userName}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""Password"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{userName.ToUpper()}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var nodeName = $"{Program.NodeExecutableName} #{clientNodeNumber}";
            try
            {

                var response = Program.UnoSysApiConnection.PostAsync(content).Result;
                result = response.StatusCode;
            }
            catch (Exception ex)
            {
                Program.DisplayUnsuccessfulWriteLine($"Error in CallUserDeleteApiOnNode(): {nodeName} - {ex} ");
            }
            return result;

        }

        //private bool NetworkAlreadyCreated()
        //{
        //    return File.Exists(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_SETTINGS_FILE_NAME));
        //}

        //private bool UserAlreadyLoggedIn()
        //{
        //    bool result = false;
        //    if (!string.IsNullOrEmpty(userName))
        //    {
        //        result = File.Exists(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_CURRENT_USERS_FILE_NAME + "_" + userName + ".json"));
        //    }
        //    else
        //    {
        //        UserSessionInfo userSessionInfo = GetUserSessionInfo();
        //        userName = userSessionInfo.UserName;
        //        result = true;
        //    }
        //    return result;
        //}

        //private UserSessionInfo GetUserSessionInfo()
        //{
        //    UserSessionInfo result = null!;
        //    string fileName = null!;
        //    if (userName == null)
        //    {
        //        fileName = Directory.GetFiles(Program.WorkingDir, Simulator.SIMULATOR_CURRENT_USERS_FILE_NAME + "_*.json")[0];
        //    }
        //    else
        //    {
        //        fileName = Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_CURRENT_USERS_FILE_NAME + "_" + userName + ".json");
        //    }
        //    using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
        //    {
        //        result = (UserSessionInfo)JsonSerializer.Deserialize<UserSessionInfo>(fs);
        //    }
        //    return result;
        //}

        //private void DeleteUserSessionInfo()
        //{
        //    File.Delete(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_CURRENT_USERS_FILE_NAME + "_" + userName + ".json"));
        //}

        //private int LoggedInUserCount()
        //{
        //    return Directory.GetFiles(Program.WorkingDir, Simulator.SIMULATOR_CURRENT_USERS_FILE_NAME + "_*.json").Length;
        //}

        //private void DeleteClusterFileSpec()
        //{
        //    File.Delete(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_CLUSTER_FILE_NAME));
        //}

        private async Task<bool> IsNodeOffline(int port)
        {
            // NOTE:  We check the connection using the TcpClient since that class allows us to set the LingerState
            //		  where as using ClientWebSocket to poll for a connection does not.  This means we won't wait 4 minutes with
            //		  the socket in a TIME_WAIT state every time we fail to connect, using TcpClient, as we would with ClientWebSocket. 
            //		  Therefore the failing socket will be torn down and returned to the socket pool immediately, allowing us to poll 
            //		  rapidly as part of the connection "healing" process.
            port = port + 2;
            bool isNodeOffLine = true;
            using (TcpClient client = new TcpClient())
            {
                // We aren't going to use the socket created as part of the TcpClient() other than to see if we can create one and if so send a small HTTP GET message.
                // We don't have to worry about "partial" data being received after it is closed (the entire purpose of entering the TIME_WAIT state) because the response to GET message is small.  
                // This allows us to set the LingerState to no time, which returns the socket to the pool of available sockets on the machine immediately.
                client.LingerState = new LingerOption(true, 0);
                try
                {
                    // NOTE:  We wish to test if the remote peer node is "up" from a Unosys perspective.
                    //        This means:
                    //			i) we can connect to it (i.e.; it is listening on IPv4Address:Port)
                    //			ii) it will return a 403 Forbidden when we try and send an HTTP GET message to a well known and up endpoint - but that does not allow GETs


                    // i)
                    // Attempt to connect to the external node
                    await client.ConnectAsync("127.0.0.1", port).ConfigureAwait(false);
                    // Prepare HTTP GET message 
                    //connectionUpDataBuffer = System.Text.Encoding.ASCII.GetBytes( string.Format( "GET http://{0}:{1}/UnosysNode/{2}/ExternalNodeManager/ HTTP/1.1\r\nHost: localhost\r\n\r\n", IPv4Address, Port, ProcessorId.ToString() ) );
                    //Debug.Print("ProcessorConnection.IsRemotePeerOffLine() {0}", string.Format("GET http://{0}:{1}/worldcomputernode/{2}/ HTTP/1.1\r\nHost: {0}\r\n\r\n\r\n", IPv4Address, Port, ProcessorId.ToString()));
                    var connectionUpDataBuffer = Encoding.UTF8.GetBytes(string.Format("GET http://{0}:{1}/worldcomputernode/{2}/ HTTP/1.1\r\nHost: {0}\r\n\r\n\r\n", "127.0.0.1", port, ""));

                    using (NetworkStream stream = client.GetStream())
                    {
                        // Send the message to the connected TcpServer. 
                        stream.Write(connectionUpDataBuffer, 0, connectionUpDataBuffer.Length);
                        // Read the first batch of the TcpServer response bytes.
                        Int32 bytesRead = stream.Read(connectionUpDataBuffer, 0, connectionUpDataBuffer.Length);
                        //String responseData = System.Text.Encoding.ASCII.GetString( connectionUpDataBuffer, 0, bytesRead );
                        String responseData = System.Text.Encoding.UTF8.GetString(connectionUpDataBuffer, 0, bytesRead).ToUpper();
                        if (!string.IsNullOrEmpty(responseData))
                        {
                            // Debug.Print("A {0}", responseData);
                            int indexPos = responseData.IndexOf("403 FORBIDDEN");
                            // Debug.Print("B {0}", indexPos);
                            if (indexPos >= 0)
                            {
                                isNodeOffLine = false;  // we are online!
                                //Debug.Print($"ProcessorConnection.IsRemotePeerOffline - remote node is online!!");
                            }
                            //else
                            //{
                            //                         Debug.Print($"$ProcessorConnection.IsRemotePeerOffline - response={responseData}");
                            //                     }
                        }
                        //else
                        //{
                        //	Debug.Print($"$ProcessorConnection.IsRemotePeerOffline - response is NULL");
                        //}
                    }
                }
                catch (Exception)
                {
                    // NOP - If we make it here then we failed to connect to the remote node presumably because it is not reachable.
                    //		 We simply ignore the error and fall out of the using() statement block
                    //		 which will close the socket and immediately return it to the available pool
                    //Debug.Print( $"ProcessorConnection.IsRemotePeerOffline - true - {ex.Message}" );
                }
                //Debug.Print( "ProcessorConnection.IsRemotePeerOffline - {0}", isRemotePeerOffline );
            }
            return isNodeOffLine;
        }
        #endregion
    }

    public class UserSessionInfo
    {
        public UserSessionInfo() { } // parameterless constructor required for deserialization

        public UserSessionInfo(string userName, string userSessionToken)
        {
            UserSessionToken = userSessionToken;
            UserName = userName;
        }

        [JsonPropertyName("UserSessionToken")]
        public string UserSessionToken { get; set; }
        [JsonPropertyName("UserName")]
        public string UserName { get; set; }

        public static UserSessionInfo WCUserSessionContext { get { return new UserSessionInfo("WorldComputerOsUser", null!); } }
    }
}
