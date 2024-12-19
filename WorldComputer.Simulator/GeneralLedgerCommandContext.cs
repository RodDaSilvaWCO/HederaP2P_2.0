using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnoSys.Api.Models;

namespace WorldComputer.Simulator
{

    public class GeneralLedgerCommandContext : ICommandContext
	{
        #region Field Members
        const int MAX_CLUSTER_COUNT = 63;
        const int MAX_NODES = 100;
        const int DEFAULT_CLIENT_NODE_NUMBER = 1;
        const int REPORT_TYPE_COUNT = 7;


        Hashtable switchSet = new Hashtable( 20 );
		string[] cmdArgs;
        string userName = null!;
        string transferToUserName = null!;
        bool reportSwitch = false;
        bool wcSwitch = false;
        //bool wcoSwitch = false;
        bool userSwitch = false;
        bool dateRangeSwitch = false;
        bool clientSimulator = false;
        bool simulatorHelp = false;
        bool[] reportTypes = null!;
        bool depositSwitch = false;
        bool withdrawSwitch = false;
        bool verboseSwitch = false;
        bool usdCurrencySwitch = false;
        bool toSwitch = false;
        bool transferSwitch = false;
        ulong fundsAmount = 0UL;
        NetworkSpec NetworkSpec = null!;
        string report = null!;

        DateTime fromDate = DateTime.MinValue;
        DateTime toDate = DateTime.MinValue;

        int clientNodeNumber = DEFAULT_CLIENT_NODE_NUMBER;

		#endregion

		#region Constructors
		public GeneralLedgerCommandContext( string[] commandContextArgs )
		{
            toDate = DateTime.UtcNow;                                                    // default to now (UTC)
            fromDate = new DateTime(toDate.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);       // default to Jan/1st of this year (UTC)

            reportTypes = new bool[REPORT_TYPE_COUNT];
            for ( int i = 0; i < reportTypes.Length; i++ ) 
            { 
                reportTypes[i] = false; 
            }

            cmdArgs = commandContextArgs;
			// Define valid context switches
			string[] allowedSwitches = new string[12];
			allowedSwitches[0] = "/REPORTS:";
            allowedSwitches[1] = "/WC";
            allowedSwitches[2] = "/DEPOSIT:";
            allowedSwitches[3] = "/WITHDRAW:";
            allowedSwitches[4] = "/TRANSFER:";
            //allowedSwitches[2] = "/WCO";
            allowedSwitches[5] = "/DATERANGE:";
            allowedSwitches[6] = "/CLIENTNODE:";
            allowedSwitches[7] = "/USER:";
            allowedSwitches[8] = "/TO:";
            allowedSwitches[9] = "/$";
            allowedSwitches[10] = "/VERBOSE";
            allowedSwitches[11] = "/?";
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

                #region Reports
                if (reportSwitch)
                {
                    GetGeneralLedgerReports();

                }
                #endregion

                #region Withdraw
                if ( withdrawSwitch )
                {
                    GeneralLedgerWithdraw();
                }
                #endregion

                #region Transfer
                if (transferSwitch)
                {
                    GeneralLedgerTransfer();
                }
                #endregion

                #region Deposit
                if (depositSwitch)
                {
                    GeneralLedgerDeposit();
                }
                #endregion 
            }
        }

        public bool ValidateContext()
        {
            bool invalidSwitch = false;
            object switchValue = null;

            #region Switch Processing
            #region Reports switch processing
            //Examples of possible legal /Report switches:  /Report:TB   /Report:IS,JE,TB,BS
            switchValue = switchSet["/REPORTS"];
            if (switchValue is string)
            {
                if (!string.IsNullOrEmpty((string)switchValue))
                {
                    string[] values = ((string)switchValue).Split(new char[] { ',' }, StringSplitOptions.None); // NOTE: We do NOT throw away empty values in order to check for invalid switches
                    if (values.Length > 0)
                    {
                        foreach (var val in values)
                        {
                            switch (val.ToUpper())
                            {
                                case "COA":
                                    {
                                        reportTypes[0] = true;
                                        break;
                                    }
                                case "TB":
                                    {
                                        reportTypes[1] = true;
                                        break;
                                    }
                                case "IS":
                                    {
                                        reportTypes[2] = true;
                                        break;
                                    }
                                case "BS":
                                    {
                                        reportTypes[3] = true;
                                        break;
                                    }
                                case "JE":
                                    {
                                        reportTypes[4] = true;
                                        break;
                                    }
                                case "GL":
                                    {
                                        reportTypes[5] = true;
                                        break;
                                    }
                                case "JEA":
                                    {
                                        reportTypes[6] = true;
                                        break;
                                    }
                                //case "GLA":
                                //    {
                                //        reportTypes[7] = true;
                                //        break;
                                //    }

                                default:
                                    {
                                        throw new CommandLineToolInvalidSwitchArgumentException($"/REPORTS: - unknown report '{val}' - must be one of 'TB' (Trial Balance), 'IS' (Income Statement), 'BS' (Balance Sheet), 'JE' (Journal Entries), 'GL' (General Ledger), or 'JEA' (Journal Entries Audit).");
                                    }
                            }
                        }
                        reportSwitch = true;
                    }
                    else
                    {
                        throw new CommandLineToolInvalidSwitchArgumentException("/REPORTS: - missing report type.  Must be one or more of 'COA' (Chart of Accounts), 'TB' (Trial Balance), 'IS' (Income Statement), 'BS' (Balance Sheet), 'JE' (Journal Entries), 'GL' (General Ledger), or 'JEA' (Journal Entries Audit).");
                    }
                    if (invalidSwitch)
                    {
                        throw new CommandLineToolInvalidSwitchException("/REPORTS:" + switchValue);
                    }
                }
            }
            else
            {
                if ((bool)switchValue)
                {
                    throw new CommandLineToolInvalidSwitchArgumentException("/REPORTS: - missing report type - must be one of 'COA' (Chart of Accounts), 'TB'(Trial Balance), 'IS'(Income Statement), 'BS'(Balance Sheet)'JE' (Journal Entries), 'GL' (General Ledger), 'JEA' (Journal Entries Audit), or 'JEA' (Journal Entries Audit).");
                }

            }
            #endregion

            #region Deposit switch processing
            //Examples of possible legal /Deposit switches:  /Deposit:Amount
            switchValue = switchSet["/DEPOSIT"];
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
                        if (!ulong.TryParse(values[0], out fundsAmount))
                        {
                            throw new CommandLineToolInvalidSwitchArgumentException($"/DEPOSIT: - Invalid Amount '{values[0]}' - must be > 0 and have no decimal places.");
                        }
                        depositSwitch = true;
                    }
                    if (invalidSwitch)
                    {
                        throw new CommandLineToolInvalidSwitchException("/DEPOSIT:" + switchValue);
                    }
                }
            }
            else
            {
                if ((bool)switchValue)
                {
                    throw new CommandLineToolInvalidSwitchArgumentException("/DEPOSIT: - missing Amount.");
                }

            }
            #endregion

            #region Withdrawl switch processing
            //Examples of possible legal /Withdraw switches:  /Withdraw:Amount
            switchValue = switchSet["/WITHDRAW"];
            if (switchValue is string)
            {
                if (!string.IsNullOrEmpty((string)switchValue))
                {
                    string[] values = ((string)switchValue).Split(new char[] { ',' }, StringSplitOptions.None); // NOTE: We do NOT throw away empty values in order to check for invalid switches
                    if (values.Length == 0)
                    {
                        fundsAmount = ulong.MaxValue;  // Signals to Withdraw ALL funds in Bank
                        withdrawSwitch = true;
                    }
                    else if( values.Length != 1 )
                    { 
                        invalidSwitch = true;
                    }
                    else
                    {
                        if (!ulong.TryParse(values[0], out fundsAmount))
                        {
                            throw new CommandLineToolInvalidSwitchArgumentException($"/WITHDRAW: - Invalid Amount '{values[0]}' - must be > 0 and have no decimal places.");
                        }
                        withdrawSwitch = true;
                    }
                    if (invalidSwitch)
                    {
                        throw new CommandLineToolInvalidSwitchException("/WITHDRAW:" + switchValue);
                    }
                }
            }
            else
            {
                if ((bool)switchValue)
                {
                    fundsAmount = ulong.MaxValue;
                    withdrawSwitch = true;
                }
                //else
                //{ 
                //    throw new CommandLineToolInvalidSwitchArgumentException("/WITHDRAW: - missing Amount in TBar.");
                //}
            }
            #endregion

            #region Transfer switch processing
            //Examples of possible legal /TRANSFER switches:  /Transfer:Amount
            switchValue = switchSet["/TRANSFER"];
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
                        if (!ulong.TryParse(values[0], out fundsAmount))
                        {
                            throw new CommandLineToolInvalidSwitchArgumentException($"/TRANSFER: - Invalid Amount '{values[0]}' - must be > 0 and have no decimal places.");
                        }
                        transferSwitch = true;
                    }
                    if (invalidSwitch)
                    {
                        throw new CommandLineToolInvalidSwitchException("/TRANSFER:" + switchValue);
                    }
                }
            }
            else
            {
                if ((bool)switchValue)
                {
                    throw new CommandLineToolInvalidSwitchArgumentException("/TRANSFER: - missing Amount.");
                }

            }
            #endregion

            #region WC switch processing
            //Examples of possible legal /WC switches:  /WC
            switchValue = switchSet["/WC"];
            if (switchValue is bool)
            {
                wcSwitch = (bool)switchValue;
            }
            #endregion

            #region Verbose switch processing
            //Examples of possible legal /VERBOSE switches:  /VERBOSE
            switchValue = switchSet["/VERBOSE"];
            if (switchValue is bool)
            {
                verboseSwitch = (bool)switchValue;
            }
            #endregion

            #region $ switch processing
            //Examples of possible legal /$ switches:  /$
            switchValue = switchSet["/$"];
            if (switchValue is bool)
            {
                usdCurrencySwitch = (bool)switchValue;
            }
            #endregion

            //#region WCO switch processing
            ////Examples of possible legal /WCO switches:  /WCO
            //switchValue = switchSet["/WCO"];
            //if (switchValue is bool)
            //{
            //    wcoSwitch = (bool)switchValue;
            //}
            //#endregion

            #region Date Range switch processing
            //Examples of possible legal /DateRange switches:  /DateRange  /DateRange:  /DateRange:<fromDate (YYYYMMDD)>  /DateRnage:,<toDate (YYYYMMDD)>  /DateRange:<fromDate (YYYYMMDD)>,<toDate (YYYYMMDD )   
            switchValue = switchSet["/DATERANGE"];
            if (switchValue is string)
            {
                if (!string.IsNullOrEmpty((string)switchValue))
                {
                    string[] values = ((string)switchValue).Split(new char[] { ',' }, StringSplitOptions.None); // NOTE: We do NOT throw away empty values in order to check for invalid switches
                    switch (values.Length)
                    {
                        case 0:
                            {
                                // defaults for from/to dates are used
                                break;
                            }

                        case 1:
                            if (!string.IsNullOrEmpty(values[0]))
                            {
                                if (!DateTime.TryParseExact(values[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out fromDate))
                                {
                                    throw new CommandLineToolInvalidSwitchArgumentException($"/DATERANGE: - invalid From Date '{values[0]}' - must be in the form YYYYMMDD");
                                }
                            }
                            dateRangeSwitch = true;
                            break;
                        case 2:
                            if (!string.IsNullOrEmpty(values[0]))
                            {
                                if (!DateTime.TryParseExact(values[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out fromDate))
                                {
                                    throw new CommandLineToolInvalidSwitchArgumentException($"/DATERANGE: - invalid From Date '{values[0]}' - must be in the form YYYYMMDD");
                                }
                                if (!string.IsNullOrEmpty(values[1]))
                                {
                                    if (!DateTime.TryParseExact(values[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out toDate))
                                    {
                                        throw new CommandLineToolInvalidSwitchArgumentException($"/DATERANGE: - invalid To Date '{values[1]}' - must be in the form YYYYMMDD");
                                    }
                                }
                                dateRangeSwitch = true;
                            }
                            break;
                        default:
                            invalidSwitch = true;
                            break;
                    }
                    if (invalidSwitch)
                    {
                        throw new CommandLineToolInvalidSwitchException("/DATERANGE:" + switchValue);
                    }
                }
            }
            else
            {
                if ((bool)switchValue)
                {
                    throw new CommandLineToolInvalidSwitchArgumentException("/DATERANGE: - missing From and/or To Date argument(s).");
                }
            }
            #endregion

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

            #region Transfer TO switch processing
            switchValue = switchSet["/TO"];
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
                        transferToUserName = values[0];
                        toSwitch = true;
                    }
                    if (invalidSwitch)
                    {
                        throw new CommandLineToolInvalidSwitchException("/TO:" + switchValue);
                    }
                }
            }
            else
            {
                if ((bool)switchValue)
                {
                    throw new CommandLineToolInvalidSwitchException("/TO:" + switchValue);
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
            if (fromDate.Year != toDate.Year || fromDate.Year != DateTime.UtcNow.Year)
            {
                throw new CommandLineToolInvalidSwitchArgumentException($"/DATERANGE: - dates must be in the current year.");
            }
            if (toDate < fromDate)
            {
                throw new CommandLineToolInvalidSwitchArgumentException($"/DATERANGE: - From Date must be greater than or equal to To Date.");
            }

            if (clientSimulator && !reportSwitch)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/CLIENTNODE can only be used with the /REPORT switch.");
            }

            if (transferSwitch && !toSwitch)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/TO:<User> must be specified along with the /TRANSFER switch.");
            }

            //      if ( wcoSwitch && wcSwitch)
            //{
            // throw new CommandLineToolInvalidSwitchCombinationException( "/WC and /WCO cannot be used together.");
            //}
            if (withdrawSwitch && depositSwitch)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/DEPOSIT and /WITHDRAW cannot be used together.");
            }

            if (withdrawSwitch && transferSwitch)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/TRANSFER and /WITHDRAW cannot be used together.");
            }

            if (wcSwitch && transferSwitch)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/TRANSFER and /WC cannot be used together.");
            }

            if (depositSwitch && transferSwitch)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/TRANSFER and /DEPOSIT cannot be used together.");
            }

            if (reportSwitch && withdrawSwitch)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/REPORTS and /WITHDRAW cannot be used together.");
            }

            if (reportSwitch && transferSwitch)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/REPORTS and /TRANSFER cannot be used together.");
            }

            if (reportSwitch && depositSwitch)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("/REPORTS and /DEPOSIT cannot be used together.");
            }

            if (!reportSwitch && !simulatorHelp && !depositSwitch && !withdrawSwitch && !transferSwitch)
            {
                throw new CommandLineToolInvalidSwitchCombinationException("One of /REPORTS, /DEPOSIT, /WITHDRAW or /TRANSFER is required.");
            }
            #endregion

            bool result = (reportSwitch || dateRangeSwitch || userSwitch || withdrawSwitch || depositSwitch || transferSwitch || toSwitch || wcSwitch || clientSimulator || verboseSwitch || usdCurrencySwitch || simulatorHelp);
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
			Program.DisplaySuccessfulWriteLine( "WorldComputer GLEDGER command usage:" );
            Program.DisplaySuccessfulWriteLine( "" );
            Program.DisplaySuccessfulWriteLine( "      WCSim GLEDGER | GL  [<switches>]" );
			Program.DisplaySuccessfulWriteLine( "" );
			Program.DisplaySuccessfulWriteLine( "where <switches> are one or more of:" );
			Program.DisplaySuccessfulWriteLine( "" );
            Program.DisplaySuccessfulWriteLine($" /CLIENTNODE:<Node#>\t\t\tClient Node # to connect to - ({DEFAULT_CLIENT_NODE_NUMBER})");
            //Program.DisplaySuccessfulWriteLine( " /DATERANGE:[[FromDate][,[ToDate]]]\tFrom,To date range as YYYYMMDD (Jan 1 current year to today)");
            Program.DisplaySuccessfulWriteLine( " /DEPOSIT:<Amount>\t\t\tTransfer Amount of funds to Bank from Crypto Account");
            Program.DisplaySuccessfulWriteLine( " /REPORTS:COA|TB|IS|BS|JE|GL|JEA\t\tGenerate General Ledger reports");
            Program.DisplaySuccessfulWriteLine( " /TO:<UserName>\t\t\t\tUser to TRANSFER funds to");
            Program.DisplaySuccessfulWriteLine( " /TRANSFER:[<Amount>]\t\t\tTransfer Amount of funds to User ");
            Program.DisplaySuccessfulWriteLine( " /USER:<UserName>\t\t\tEstablish logged in User context to execute operation in");
            Program.DisplaySuccessfulWriteLine( " /VERBOSE\t\t\t\tShow DLT Accounts");
            Program.DisplaySuccessfulWriteLine( " /WC\t\t\t\t\tWorld Computer context");
            Program.DisplaySuccessfulWriteLine( " /WITHDRAW:[<Amount>]\t\t\tTransfer Amount of funds from Bank to Crypto Account (All)");
            Program.DisplaySuccessfulWriteLine( " /$\t\t\t\t\tAmounts in USD");
            Program.DisplaySuccessfulWriteLine( " /?\t\t\t\t\tUsage information" );
			Program.DisplaySuccessfulWriteLine( "" );
            Program.DisplaySuccessfulWriteLine("=======================================================================================================");
        }
		#endregion


		#region Helpers
        private void GetGeneralLedgerReports()
        {
            UserSessionInfo userSessionInfo = null!;

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

            string result = "";
            #region Step #4:  Check if user already logged in
            var userLoggedIn = Simulator.UserAlreadyLoggedIn(clientNodeNumber, userName);
            if (!userLoggedIn && ! wcSwitch )
            {
                if (!string.IsNullOrEmpty(userName))
                {
                    throw new CommandLineToolInvalidOperationException($"Cannot perform operation because User {userName.ToUpper()} is not logged in.");
                }
                else
                {
                    throw new CommandLineToolInvalidOperationException($"Cannot perform operation because no User context is established - use /WC, or /USER switch, or login as a User.");
                }
            }
            else
            {
                
                if (wcSwitch)
                {
                    userSessionInfo = UserSessionInfo.WCUserSessionContext;
                }
                else if (userLoggedIn)
                {
                    #region Step #5: Deserialize UserSessionInfo to obtain the Token of the Logged in User session
                    userSessionInfo = Simulator.GetUserSessionInfo(clientNodeNumber, userName);
                    if (userSessionInfo == null ) //|| !userName.Equals(userSessionInfo.UserName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CommandLineToolInvalidOperationException($"Corrupted WCSim user file.");
                    }
                    userName = userSessionInfo.UserName;
                    #endregion
                }

                #region Determine ReportOptions from switches
                GeneralLedgerReportOptions reportOptions = GeneralLedgerReportOptions.TextOutput;
                if( usdCurrencySwitch )
                {
                    reportOptions |= GeneralLedgerReportOptions.USDCurrency;
                }
                if( verboseSwitch)
                {
                    reportOptions |= GeneralLedgerReportOptions.Verbose;
                }
                #endregion 

                #region  Step #6:  Call the WorldComputer GeneralLedgerGetReport() Api to produce the reports
                for (int i = 0; i < reportTypes.Length; i++) 
                {
                    if (reportTypes[i])
                    {
                        result += Environment.NewLine;
                        result += CallGeneralLedgerGetReportApiOnNode(userSessionInfo.UserSessionToken, i, (int)reportOptions, 
                                                fromDate.ToString("yyyyMMdd"), toDate.ToString("yyyyMMdd"));
                        result += Environment.NewLine;
                        if (i < reportTypes.Length - 1)
                        {
                            //result += "==================================================================================";
                            result += Environment.NewLine;
                        }
                    }
                }
                #endregion
            }
            #endregion

            #region Step #7 - Display result
            Program.DisplaySuccessfulWriteLine($"");
            //Program.DisplaySuccessfulWriteLine($"REPORTS for {userSessionInfo.UserName}: ");
            //Program.DisplaySuccessfulWriteLine("==================================================================================");
            //Program.DisplaySuccessfulWriteLine($"");
            Program.DisplaySuccessfulWriteLine(result);
            #endregion

        }



        private void GeneralLedgerWithdraw()
        {
            UserSessionInfo userSessionInfo = null!;

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

            #region Step #4:  Check if user already logged in
            var userLoggedIn = Simulator.UserAlreadyLoggedIn(clientNodeNumber, userName);
            if (!userLoggedIn && !wcSwitch)
            {
                if (!string.IsNullOrEmpty(userName))
                {
                    throw new CommandLineToolInvalidOperationException($"Cannot perform operation because User {userName.ToUpper()} is not logged in.");
                }
                else
                {
                    throw new CommandLineToolInvalidOperationException($"Cannot perform operation because no User context is established - use /WC, or /USER switch, or login as a User.");
                }
            }
            else
            {

                if (wcSwitch)
                {
                    userSessionInfo = UserSessionInfo.WCUserSessionContext;
                }
                else if (userLoggedIn)
                {
                    #region Step #5: Deserialize UserSessionInfo to obtain the Token of the Logged in User session
                    userSessionInfo = Simulator.GetUserSessionInfo(clientNodeNumber, userName);
                    if (userSessionInfo == null  ) //|| !userName.Equals(userSessionInfo.UserName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CommandLineToolInvalidOperationException($"Corrupted WCSim user file.");
                    }
                    userName = userSessionInfo.UserName;
                    #endregion
                }

                #region Step #6 Obtain External Crypto Address and PrivateKey from user:
                var ogrForegroundColor = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("");
                    Console.WriteLine("");
                    Console.WriteLine("Enter External Hedera Crypto Address: ");
                    Console.ForegroundColor = ConsoleColor.White;
                    string dltAddress = Console.ReadLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("");
                    Console.WriteLine("Enter External Hedera Crypto Private Key:");
                    Console.ForegroundColor = ConsoleColor.White;
                    string dltPrivateKey = Console2.ReadPassword();

                    #region Step #7 - Call CallGeneralLedgerDefundWalletApiOnNode
                    int unitOfAmount = 0;  // Default TINYBAR
                    if (usdCurrencySwitch)
                    {
                        unitOfAmount = 2;  // USD
                    }
                    var result = CallGeneralLedgerDefundWalletApiOnNode(userSessionInfo.UserSessionToken, dltAddress, dltPrivateKey, fundsAmount, unitOfAmount );
                    Console.WriteLine("");
                    Console.WriteLine("");
                    Console.WriteLine(result);
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
        }


        private void GeneralLedgerDeposit()
        {
            UserSessionInfo userSessionInfo = null!;

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

            #region Step #4:  Check if user already logged in
            var userLoggedIn = Simulator.UserAlreadyLoggedIn(clientNodeNumber, userName);
            if (!userLoggedIn && !wcSwitch)
            {
                if (!string.IsNullOrEmpty(userName))
                {
                    throw new CommandLineToolInvalidOperationException($"Cannot perform operation because User {userName.ToUpper()} is not logged in.");
                }
                else
                {
                    throw new CommandLineToolInvalidOperationException($"Cannot perform operation because no User context is established - use /WC, or /USER switch, or login as a User.");
                }
            }
            else
            {

                if (wcSwitch)
                {
                    userSessionInfo = UserSessionInfo.WCUserSessionContext;
                }
                else if (userLoggedIn)
                {
                    #region Step #5: Deserialize UserSessionInfo to obtain the Token of the Logged in User session
                    userSessionInfo =   Simulator.GetUserSessionInfo(clientNodeNumber, userName);
                    if (userSessionInfo == null ) // || !userName.Equals(userSessionInfo.UserName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CommandLineToolInvalidOperationException($"Corrupted WCSim user file.");
                    }
                    userName = userSessionInfo.UserName;
                    #endregion
                }

                #region Step #6 Obtain External Crypto Address and PrivateKey from user:
                var ogrForegroundColor = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("");
                    Console.WriteLine("");
                    Console.WriteLine("Enter External Hedera Crypto Address: ");
                    Console.ForegroundColor = ConsoleColor.White;
                    string dltAddress = Console.ReadLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("");
                    Console.WriteLine("Enter External Hedera Crypto Private Key:");
                    Console.ForegroundColor = ConsoleColor.White;
                    string dltPrivateKey = Console2.ReadPassword();

                    #region Step #7 - Call CallGeneralLedgerFundWalletApiOnNode
                    int unitOfAmount = 0;  // Default TINYBAR
                    if (usdCurrencySwitch)
                    {
                        unitOfAmount = 2;  // USD
                    }
                    var result = CallGeneralLedgerFundWalletApiOnNode(userSessionInfo.UserSessionToken, dltAddress, dltPrivateKey, fundsAmount, unitOfAmount);
                    Console.WriteLine("");
                    Console.WriteLine("");
                    Console.WriteLine(result);

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
        }


        private void GeneralLedgerTransfer()
        {
            UserSessionInfo userSessionInfo = null!;

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
                    throw new CommandLineToolInvalidOperationException($"Cannot perform operation because no User context is established - use /WC, or /USER switch, or login as a User.");
                }
            }
            else
            {
                if (userLoggedIn)
                {
                    #region Step #5: Deserialize UserSessionInfo to obtain the Token of the Logged in User session
                    userSessionInfo = Simulator.GetUserSessionInfo(clientNodeNumber, userName);
                    if (userSessionInfo == null) // || !userName.Equals(userSessionInfo.UserName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CommandLineToolInvalidOperationException($"Corrupted WCSim user file.");
                    }
                    userName = userSessionInfo.UserName;
                    #endregion
                }

                #region Step #6 - Call CallGeneralLedgerFundWalletApiOnNode
                var ogrForegroundColor = Console.ForegroundColor;
                try
                {
                    int unitOfAmount = 0;  // Default TINYBAR
                    if (usdCurrencySwitch)
                    {
                        unitOfAmount = 2;  // USD
                    }
                    var result = CallGeneralLedgerTransferFundsApiOnNode(userSessionInfo.UserSessionToken, transferToUserName, fundsAmount, unitOfAmount);
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine("");
                    Console.WriteLine("");
                    Console.WriteLine(result);
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
        
        private string CallGeneralLedgerGetReportApiOnNode(string userSessionToken, int reportType, int reportOptions, string fromUtcDate, string toUtcDate )
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
                                        ""N"" : ""ReportOptions"",
                                        ""T"" : ""System.Int32"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{reportOptions}"",
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


        private string CallGeneralLedgerFundWalletApiOnNode(string userSessionToken, string dltAddress, string dltPrivateKey, ulong amount, int unitOfAmount)
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
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""UnitOfAmount"",
                                        ""T"" : ""System.Int32"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{unitOfAmount}"",
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
                    result = $"{(unitOfAmount == 2 ? "$" : "")}{amount.ToString("N0")} {(unitOfAmount == 2 ? "worth of" : "")} TBar successfully deposited to GL of {userName.ToUpper()} from Crypto Account: {dltAddress}";
                    //result = $"{amount.ToString("N0")} TBar successfully deposited to GL of {userName.ToUpper()} from Crypto Account: {dltAddress} - Journal Entry Posted:" + Environment.NewLine + Environment.NewLine;
                    var responseJson = response.Content.ReadAsStringAsync().Result;
                    using var doc = JsonDocument.Parse(responseJson);
                    var element = doc.RootElement;
                    if (verboseSwitch)
                    {
                        result += " - Journal Entry Posted:" + Environment.NewLine + Environment.NewLine;
                        result += element.GetProperty("O")[0].GetProperty("V").GetString()!;
                    }
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

        private string CallGeneralLedgerDefundWalletApiOnNode(string userSessionToken, string dltAddress, string dltPrivateKey, ulong amount, int unitOfAmount)
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
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""UnitOfAmount"",
                                        ""T"" : ""System.Int32"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{unitOfAmount}"",
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
                    string tmp = $"{(unitOfAmount == 2 ? "$" : "")}{amount.ToString("N0")} {(unitOfAmount == 2 ? "worth of" : "")}";
                    result = $"{(amount == ulong.MaxValue ? "All" : tmp)} TBar successfully withdrawn from GL of {userName.ToUpper()} to Crypto Account: {dltAddress}";
                    //result = $"{(amount == ulong.MaxValue ? "All" : amount.ToString("N0"))} TBar successfully withdrawn from GL of {userName.ToUpper()} to Crypto Account: {dltAddress} - Journal Entry Posted:" + Environment.NewLine + Environment.NewLine;
                    var responseJson = response.Content.ReadAsStringAsync().Result;
                    using var doc = JsonDocument.Parse(responseJson);
                    var element = doc.RootElement;
                    if (verboseSwitch)
                    {
                        result += " - Journal Entry Posted:" + Environment.NewLine + Environment.NewLine;
                        result += element.GetProperty("O")[0].GetProperty("V").GetString()!;
                    }
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


        private string CallGeneralLedgerTransferFundsApiOnNode(string userSessionToken, string transferToUserName,  ulong amount, int unitOfAmount)
        {
            string result = null!;
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.GeneralLedgerTransferFunds*/753},
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
                                        ""N"" : ""FromSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{userSessionToken}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""ToUserName"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{transferToUserName}"",
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
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""UnitOfAmount"",
                                        ""T"" : ""System.Int32"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{unitOfAmount}"",
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
                    result = $"{(unitOfAmount == 2 ? "$" : "")} {amount.ToString("N0")}{(unitOfAmount == 2 ? "worth of" : "")} TBar successfully transfered from Bank of {userName.ToUpper()} to Bank of {transferToUserName.ToUpper()}";
                    var responseJson = response.Content.ReadAsStringAsync().Result;
                    using var doc = JsonDocument.Parse(responseJson);
                    var element = doc.RootElement;
                    if (verboseSwitch)
                    {
                        result += " - Journal Entry Posted:" + Environment.NewLine + Environment.NewLine;
                        result += element.GetProperty("O")[0].GetProperty("V").GetString()!;
                    }
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
                    Program.DisplaySuccessfulWriteLine($"Error calling GeneralLedgerTransferFunds() - response.StatusCode={response.StatusCode}{(reason != null ? $" - ({reason})" : "")}");
                }
            }
            catch (Exception ex)
            {
                Program.DisplayUnsuccessfulWriteLine($"Error in CallGeneralLedgerTransferFundsApiOnNode(): {nodeName} - {ex} ");
            }
            return result;

        }

       
        #endregion
    }

   
}
