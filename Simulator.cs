using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace WorldComputer.Simulator
{
	internal class Simulator : CommandLineTool
	{
		// Switches
		private bool netContext;
		private bool vdContext;
		private bool clusterContext;
		private bool ledgerContext;
		private bool mediaContext;
		private bool userContext;
		private bool help;
		private ICommandContext commandContext;
		private string[] commands;
		static internal string SIMULATOR_SETTINGS_FILE_NAME = "WCSim.json";
		static internal string SIMULATOR_PROCESSOR_EXE_NAME = "WCNode";
		static internal string SIMULATOR_VDRIVE_FILE_NAME = "WCSimVDrive.json";
		static internal string SIMULATOR_CLUSTER_FILE_NAME = "WCSimCluster.json";
		//static internal string SIMULATOR_CURRENT_USERS_FILE_NAME = "WCSimCurrentUser";
        static internal string SIMULATOR_CURRENT_USERS_FILE_NAME_TEMPLATE = "WCSimCurrentUser_{0}_{1}.json";
        static internal string SIMULATOR_CURRENT_ALL_USERS_FILE_NAME_TEMPLATE = "WCSimCurrentUser*_{0}.json";
        static internal int MAX_NETWORK_NODE_SIZE = 100;


		internal Simulator()
			: base("WorldComputer Simulator (WCSim) Utility")
		{


			// Define valid commands
			commands = new string[6];
			commands[0] = "NODE    | N";
            //commands[1] = "CLUSTER | C";
            commands[1] = "GLEDGER | GL";
			commands[2] = "MEDIA   | M";
            commands[3] = "USER    | U";
            commands[4] = "VDRIVE  | VD";
			commands[5] = "HELP    | ?";
		}

		internal void ProcessCommand(string[] args)
		{

			if (args.Length == 0)
			{
				// Invalid commandline - show usage
				CommandLineTool.DisplayBanner();
				this.DisplayUsage();
			}
			else
			{
				string restOfCommandLine = string.Empty;
				// Determine command to run
				this.ParseCommandLineCommand(args, commands, ref restOfCommandLine);
				netContext = (bool)Commands["NODE"] || (bool)Commands["N"];
				vdContext = (bool)Commands["VDRIVE"] || (bool)Commands["VD"];
                //clusterContext = (bool)Commands["CLUSTER"] || (bool)Commands["C"];
                ledgerContext = (bool)Commands["GLEDGER"] || (bool)Commands["GL"];
                userContext = (bool)Commands["USER"] || (bool)Commands["U"];
				mediaContext = (bool)Commands["MEDIA"] || (bool)Commands["M"];
                help = (bool)Commands["HELP"] || (bool)Commands["?"];

				// Process commands
				if (help)
				{
					CommandLineTool.DisplayBanner();
					this.DisplayUsage();
				}
				else
				{
					string[] commandContextArgs = restOfCommandLine.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
					if (netContext)
					{
						commandContext = new NetworkCommandContext(commandContextArgs);
					}
					else if (vdContext)
					{
						commandContext = new VirtualDriveCommandContext(commandContextArgs);
					}
					else if( ledgerContext )
					{
						commandContext = new GeneralLedgerCommandContext(commandContextArgs);
					}
					else if (mediaContext)
					{
						commandContext = new MediaCommandContext(commandContextArgs);
					}
					else if (userContext)
					{
						commandContext = new UserCommandContext(commandContextArgs);
					}
					if (commandContext.ValidateContext())
					{
						commandContext.ProcessCommand();
					}

				}
			}
		}

		public override void DisplayUsage()
		{
			Program.DisplaySuccessfulWriteLine("WorldComputer Simulator (WCSim) usage:");
			Program.DisplaySuccessfulWriteLine("");
			Program.DisplaySuccessfulWriteLine("        WCSim <command>");
			Program.DisplaySuccessfulWriteLine("");
			Program.DisplaySuccessfulWriteLine("where <command> is one of:");
			Program.DisplaySuccessfulWriteLine("");
			Program.DisplaySuccessfulWriteLine("        NODE    | N\t<swithces> - Manage a Node network");
            Program.DisplaySuccessfulWriteLine("        GLEDGER | GL\t<swithces> - Manage the World Computer General Ledger");
            Program.DisplaySuccessfulWriteLine("        MEDIA   | M\t<swithces> - Manage User's Media access");
            Program.DisplaySuccessfulWriteLine("        USER    | U\t<swithces> - Manage current User session");
			Program.DisplaySuccessfulWriteLine("        VDRIVE  | VD\t<swithces> - Manage a Virtual Drive backed by nodes");
			Program.DisplaySuccessfulWriteLine("        HELP    | ?\tUsage information");
			Program.DisplaySuccessfulWriteLine("");
            Program.DisplaySuccessfulWriteLine("=======================================================================================================");
		}


		~Simulator()
		{
			this.Dispose();
		}

		public void Dispose()
		{
		}

        internal static  bool UserAlreadyLoggedIn(int clientNode, string userName)
        {
            bool result = false;
            if (!string.IsNullOrEmpty(userName))
            {
                result = File.Exists(Path.Combine(Program.WorkingDir, 
					string.Format( SIMULATOR_CURRENT_USERS_FILE_NAME_TEMPLATE, userName, clientNode)));
            }
            else 
            {
                UserSessionInfo userSessionInfo = GetUserSessionInfo(clientNode, userName);
                userName = userSessionInfo.UserName;
                result = true;
            }
            return result;
        }

        internal static bool VirtualDriveAlreadyCreated()
        {
            return File.Exists(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_VDRIVE_FILE_NAME));
        }

		internal static VDriveInfo GetVirtualDriveSpec()
		{
            using (FileStream fs = new FileStream(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_VDRIVE_FILE_NAME), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return (VDriveInfo)JsonSerializer.Deserialize<VDriveInfo>(fs);
            }
        }

		internal static NetworkSpec GetNetworkSpec()
		{
            using (FileStream fs = new FileStream(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_SETTINGS_FILE_NAME), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return (NetworkSpec)JsonSerializer.Deserialize<NetworkSpec>(fs);
            }
        }

		internal static void DeleteVirtualDriveFileSpec()
		{
			File.Delete(Path.Combine(Program.WorkingDir, Simulator.SIMULATOR_VDRIVE_FILE_NAME));
		}

		internal static  UserSessionInfo GetUserSessionInfo(int clientNode, string userName)
        {
            UserSessionInfo result = null!;
            string fileName = null!;
            if (userName == null)
            {
				var fileSpec = string.Format(SIMULATOR_CURRENT_ALL_USERS_FILE_NAME_TEMPLATE, clientNode);
                var loggedInUsers = Directory.GetFiles(Program.WorkingDir, fileSpec);
                if (loggedInUsers != null && loggedInUsers.Length == 1)
                {
                    fileName = loggedInUsers[0];
                }
                else
                {
					if(loggedInUsers.Length > 1)
						throw new CommandLineToolInvalidOperationException($"Cannot perform operation because more than one user is logged in - use /USER switch to specify User.");
					else
                        throw new CommandLineToolInvalidOperationException($"Cannot perform operation because more no user is logged in - use /USER switch to specify User.");
                }
            }
            else
            {
                fileName = Path.Combine(Program.WorkingDir, string.Format(SIMULATOR_CURRENT_USERS_FILE_NAME_TEMPLATE, userName, clientNode));
            }
            using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                result = (UserSessionInfo)JsonSerializer.Deserialize<UserSessionInfo>(fs);
            }
            return result;
        }

        internal static void DeleteUserSessionInfo(int clientNode, string userName)
        {
            File.Delete(Path.Combine(Program.WorkingDir, string.Format(SIMULATOR_CURRENT_USERS_FILE_NAME_TEMPLATE, userName, clientNode)));
        }

		internal static void DeleteAllLoggedInUsers(int clientNode)
		{
            foreach (var file in Directory.GetFiles(Program.WorkingDir, string.Format(SIMULATOR_CURRENT_ALL_USERS_FILE_NAME_TEMPLATE, clientNode)))
            {
                System.IO.File.Delete(file);
            }
        }

        internal static int LoggedInUserCount(int clientNode)
        {
			return Directory.GetFiles(Program.WorkingDir, string.Format(SIMULATOR_CURRENT_ALL_USERS_FILE_NAME_TEMPLATE, clientNode)).Length;
        }

		internal static void PersistUserSessionToken(int clientNode, string userName, string userSessionToken )
		{
            using (var currentUserStream = new FileStream(Path.Combine(Program.WorkingDir,
                        string.Format(SIMULATOR_CURRENT_USERS_FILE_NAME_TEMPLATE, userName, clientNode)), 
						FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var userSessionInfo = new UserSessionInfo(userName, userSessionToken);
                JsonSerializer.Serialize<UserSessionInfo>(currentUserStream, userSessionInfo);
                currentUserStream.Flush();
            }
        }

        internal static bool NetworkAlreadyCreated()
        {
            return File.Exists(Path.Combine(Program.WorkingDir, SIMULATOR_SETTINGS_FILE_NAME));
        }

        internal static bool MediaServerAlreadyRunning()
        {
            bool result = false;
            var mediaServerExecutable = Program.MediaPlayerAppName.ToUpper();
			var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    if (p.ProcessName.ToUpper() + ".EXE" == mediaServerExecutable) 
                    {
                        // Found it running...
                        result = true;
                        break;
                    }
                }
                catch (Exception)
                {
                    // NOP and continue
                }
            }
            return result;
        }
    }
}
