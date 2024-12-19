namespace UnoSysKernel
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UnoSys.Api.Models;
    using UnoSysCore;
    using System.Runtime.Serialization;
    using System.Security;


    public class GeneralLedgerManager : SecuredKernelService, IGeneralLedgerManager, ICriticalShutdown
    {
        #region Field Members
        private IGeneralLedgerInstanceManifest generalLedgerInstanceManifest = null!;
        private IGeneralLedgerAccountsCatalog glCatalog = null!;
        private List<IChartOfAccountsTemplate> coaTemplateList = null!;
        private IHederaDLTContext _hederaDLTContext = null!;
        private IGeneralLedgerPersistenceProvider persistenceProvider = null!;
        private IGlobalPropertiesContext _globalPropertiesContext = null!;
        private IGeneralLedgerContext _generalLedgerContext = null!;
        private IGlobalEventSubscriptionManager _globalEventSubscriptionManager = null!;
        private ILocalNodeContext _localNodeContext = null!;
        private Stream _journalEntriesFileStream = null!;
        private ITime timeManager = null!;
        private Guid onGL_JOURNAL_ENTRY_POSTeventTargetID;
        private ISecurityContext _securityContext = null!;
        #endregion

        #region Constructors
        public GeneralLedgerManager(ITime timemanager,
                                    IGlobalPropertiesContext gProps,
                                    ILoggerFactory loggerFactory,
                                    IKernelConcurrencyManager concurrencyManager,
                                    IGeneralLedgerContext glcontext,
                                    IGlobalEventSubscriptionManager gesubmanager,
                                    ILocalNodeContext localNodeContext,
                                    IHederaDLTContext hederaDLTContext,
                                    ISecurityContext securityContext)
                    : base(loggerFactory.CreateLogger("GeneralLedgerManager"), concurrencyManager)
        {
            timeManager = timemanager;
            _hederaDLTContext = hederaDLTContext;
            coaTemplateList = _hederaDLTContext.CoaAccountsTemplate.ToList<IChartOfAccountsTemplate>();
            glCatalog = _hederaDLTContext.GeneralLedgerAccountsCatalog; 
            persistenceProvider = _hederaDLTContext.PersistenceProvider; 
            generalLedgerInstanceManifest = _hederaDLTContext.GeneralLedgerInstanceManifest;
            _globalPropertiesContext = gProps;
            _generalLedgerContext = glcontext;
            _globalEventSubscriptionManager = gesubmanager;
            _localNodeContext = localNodeContext!;
            _securityContext = securityContext;
            if (securityContext != null)  // NOTE:  securityContext will be null when spawning the Genesis node!
            {
                // Create a secured object for this kernel service 
                securedObject = securityContext.CreateDefaultKernelServiceSecuredObject();
            }

        }
        #endregion

        #region IKernelService Implementation
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            using (var exclusiveLock = await _concurrencyManager.KernelLock.WriterLockAsync().ConfigureAwait(false))
            {
                #region Check for and Open/Read Connected GL Journal Entries local file to populate Peer Group list
                if (_globalPropertiesContext != null)
                {
                    // NOTE:  _globalPropertiesContext will be null if spawning Genesis node in which case we skip the open/read of the GL JEs file
                    string journalEntriesFileName = null!;
                    if (_globalPropertiesContext.IsIntendedForSimulation && _globalPropertiesContext.SimulationNodeNumber > 0)
                    {
                        journalEntriesFileName = HostCryptology.ConvertBytesToHexString(HostCryptology.Encrypt2(Encoding.ASCII.GetBytes(
                                    $"JE{HostCryptology.ConvertBytesToHexString(_generalLedgerContext.WorldComputerSymmetricKey)}" +
                                       _globalPropertiesContext.SimulationPool![_globalPropertiesContext.SimulationNodeNumber].ToString("N").ToUpper()),
                                       _generalLedgerContext.NodeSymmetricKey, _generalLedgerContext.NodeSymmetricIV));
                    }
                    else
                    {
                        journalEntriesFileName = HostCryptology.ConvertBytesToHexString(HostCryptology.Encrypt2(Encoding.ASCII.GetBytes(
                                    $"JE{HostCryptology.ConvertBytesToHexString(_generalLedgerContext.WorldComputerSymmetricKey)}" +
                                        _generalLedgerContext.NodeID.ToString("N").ToUpper()), _generalLedgerContext.NodeSymmetricKey, _generalLedgerContext.NodeSymmetricIV));
                    }
                    var journalEntriesFilePath = Path.Combine(Path.Combine(_globalPropertiesContext.NodeDirectoryPath, _generalLedgerContext.LocalStoreDirectoryName), journalEntriesFileName);
                    if (!File.Exists(journalEntriesFilePath))
                    {
                        if (_globalPropertiesContext.IsIntendedForSimulation && _globalPropertiesContext.SimulationNodeNumber > 0)
                        {

                            // Assume first time Simulation Node is being run so create the missing file with a file wtih no entries
                            _journalEntriesFileStream = new FileStream(journalEntriesFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                        }
                        else
                        {
                            // For a regular node the file should have been created by spawn, so error
                            throw new System.Exception($"The Node's Journal Entry subscription file was not found - shutting down.");
                        }
                    }
                    else
                    {
                        // If we make it here we simply open the existing Journal Entries file
                        _journalEntriesFileStream = new FileStream(journalEntriesFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                }
                #endregion

                #region Register to receive global GL_JOURNAL_ENTRY_POST event
                if (_globalEventSubscriptionManager != null)
                {
                    // NOTE: _globalEventSubscriptionManager will be null when spawning Genesis node in which case we skip global subscriptions
                    onGL_JOURNAL_ENTRY_POSTeventTargetID = _globalEventSubscriptionManager!.RegisterEventTarget(On_GL_JOURNAL_ENTRY_POST_Event, GlobalEventType.GL_JOURNAL_ENTRY_POST);
                }
                #endregion
                await base.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            using (var exclusiveLock = await _concurrencyManager.KernelLock.WriterLockAsync().ConfigureAwait(false))
            {
                #region  Unregister to stop receiving global GL_JOURNAL_ENTRY_POST events 
                if (_globalEventSubscriptionManager != null)
                {
                    // NOTE: _globalEventSubscriptionManager will be null when spawning Genesis node in which case we skip global subscriptions
                    _globalEventSubscriptionManager!.UnregisterEventTarget(onGL_JOURNAL_ENTRY_POSTeventTargetID);
                }
                #endregion

                #region Flush Journal Entries file contents and close
                if (_globalPropertiesContext != null)
                {
                    // NOTE: _globalEventSubscriptionManager will be null when spawning Genesis node in which case we we skip closing the GL JEs file
                    _journalEntriesFileStream?.Flush();
                    _journalEntriesFileStream?.Close();
                    _journalEntriesFileStream?.Dispose();
                    _journalEntriesFileStream = null!;
                }
                #endregion
                await base.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        #endregion

        #region ICriticalShutdown Implementaiton
        public async Task CriticalStopAsync(CancellationToken cancellationToken)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }
        #endregion

        #region IDisposable Implementation
        public override void Dispose()
        {
            _globalPropertiesContext = null!;
            glCatalog = null!;
            coaTemplateList = null!;
            _hederaDLTContext = null!;
            persistenceProvider = null!;
            _generalLedgerContext = null!;
            timeManager = null!;
            _localNodeContext = null!;
            _journalEntriesFileStream = null!;
            _securityContext = null!;
            _globalEventSubscriptionManager = null!;
        }
        #endregion


        #region Public Interface
        public async Task<decimal> GetCurrentHBarUSDExchangeRateAsync()
        {
            return await _hederaDLTContext.GetCurrentHBarUSDExchangeRateAsync().ConfigureAwait(false);
        }

        public async Task<IGeneralLedgerInstanceManifest> GenerateInstanceManifestAsync(string wcoId, string wcId, IGeneralLedgerProperties _glProperties )
        {
            // NOTE:  This method is ONLY EVER called during the Spawning of the *** GENESIS *** node!
            generalLedgerInstanceManifest = null!;
            try
            {
                GeneralLedger jurisdictionGeneralLedger = null!;
                GeneralLedger wcoGeneralLedgerServiceGeneralLedger = null!;

                #region Get the crypto account balance of the payor before we start
                var cryptoPayorBefore = await _hederaDLTContext.GetCryptoAccountBalanceAsync().ConfigureAwait(false);
                if (cryptoPayorBefore.HasValue)
                {
                    if (cryptoPayorBefore.Value < 8_000_000)
                    {
                        throw new DLTInsufficientPayerFundsException();
                    }
                    else
                    {
                        Debug.Print($"Crypto Balance of GL Payor Account  = {cryptoPayorBefore}.");
                    }
                }
                else
                {
                    Debug.Print($"Could not obtain Crypto Balance of GL Payor Account.");
                }
                #endregion

                #region Generate Required GL Treasury Account Address with Keys 
                DLTAddress? glTreasuryAccount = (DLTAddress?)await _hederaDLTContext.CreateCryptoAccountAsync().ConfigureAwait(false);
                #endregion

                #region Generate Required General Ledgers
                if (glTreasuryAccount != null)
                {
                    Debug.Print($"Generated JurisdictionTreasuryAddress: {glTreasuryAccount.AddressID}");
                    Debug.Print($"Generated JurisdictionTreasuryAddressPublicKey: {glTreasuryAccount.KeyVault!.Base64EncryptedPublicKey}");
                    Debug.Print($"Generated JurisdictionTreasuryAddressPrivateKey: {glTreasuryAccount.KeyVault!.Base64EncryptedPrivateKey}");
                    #region IGNORE : Get the crypto account balance of the newly created GL Treasury Account (should be zero)
                    //var cryptoBalance = await _hederaDLTContext.GetCryptoAccountBalanceAsync(glTreasuryAccount).ConfigureAwait(false);
                    //if (cryptoBalance.HasValue)
                    //{
                    //    Debug.Print($"Crypto Balance of GL Treasury Account {glTreasuryAccount.AddressID} = {cryptoBalance}.");
                    //}
                    //else
                    //{
                    //    Debug.Print($"Could not obtain Crypto Balance of GL Treasury Account {glTreasuryAccount.AddressID}.");
                    //}
                    #endregion 

                    #region Generate required keys for Jurisdiction GL
                    var glTokenAdminKeys = (KeyVault)_hederaDLTContext.CreateKeys(_public: true, _private: true);
                    //var glTokenGrantKycKeys = (KeyVault)_hederaDLTContext.CreateKeys(_public: true, _private: true);
                    //var glTokenSuspendKeys = (KeyVault)_hederaDLTContext.CreateKeys(_public: true, _private: true);
                    //var glTokenConfiscateKeys = (KeyVault)_hederaDLTContext.CreateKeys(_public: true, _private: true);
                    //var glTokenSupplyKeys = (KeyVault)_hederaDLTContext.CreateKeys(_public: true, _private: true);
                    #endregion

                    #region Generate Jurisdiction GL Token
                    DLTToken glToken = (DLTToken)await _hederaDLTContext.CreateTokenAsync( glTreasuryAccount, glTokenAdminKeys //,
                                                                                                        //glTokenGrantKycKeys,
                                                                                                        //glTokenSuspendKeys,
                                                                                                        //glTokenConfiscateKeys,
                                                                                                        //glTokenSupplyKeys
                                                                                        );
                    #endregion
                    if (glToken != null && glToken.Address != null && !string.IsNullOrEmpty(glToken.Address.AddressID))
                    {
                        Debug.Print($"Generated JurisdictionTokenAddress: {glToken.Address.AddressID}");
                        Debug.Print($"Generated JurisdictionTokenAdminPublicKey: {glTokenAdminKeys.Base64EncryptedPublicKey}");
                        Debug.Print($"Generated JurisdictionTokenAdminPrivateKey: {glTokenAdminKeys.Base64EncryptedPrivateKey}");
                        #region IGNORE:  Check the Token Balance on the GL Treasury account to ensure it is funded
                        //var tokenBalance = await _hederaDLTContext.GetTokenAccountBalanceAsync(glToken, glTreasuryAccount).ConfigureAwait(false);
                        //if (tokenBalance.HasValue)
                        //{
                        //    Debug.Print($"Token {glToken.Name} Balance of GL Treasury Account {glTreasuryAccount.AddressID} = {tokenBalance}.");
                        //}
                        //else
                        //{
                        //    Debug.Print($"Could not obtain Token Balance of GL Treasury Account {glTreasuryAccount.AddressID}.");
                        //}
                        #endregion

                        #region Determine required COA Templates
                        var coaJurisdictionTemplate = GLAccountUtilities.LookupCoaTemplate(coaTemplateList, _glProperties.JurisdictionChartOfAccountTemplateId!);
                        if (coaJurisdictionTemplate == null || coaJurisdictionTemplate.ChartOfAccounts == null)
                        {
                            throw new InvalidOperationException($"Unknown Chart of Account template id {_glProperties.JurisdictionChartOfAccountTemplateId!}");
                        }
                        var coaMemberTemplate = GLAccountUtilities.LookupCoaTemplate(coaTemplateList, _glProperties.JurisdictionMemberChartOfAccountTemplateId!);
                        if (coaMemberTemplate == null || coaMemberTemplate.ChartOfAccounts == null)
                        {
                            throw new InvalidOperationException($"Unknown Chart of Account template id {_glProperties.JurisdictionMemberChartOfAccountTemplateId!}");
                        }
                        var coaWcoGeneralLedgerServiceTemplate = GLAccountUtilities.LookupCoaTemplate(coaTemplateList, _glProperties.WCOGeneralLedgerServiceChartOfAccountTemplateId!);
                        if (coaWcoGeneralLedgerServiceTemplate == null || coaWcoGeneralLedgerServiceTemplate.ChartOfAccounts == null)
                        {
                            throw new InvalidOperationException($"Unknown Chart of Account template id {_glProperties.WCOGeneralLedgerServiceChartOfAccountTemplateId!}");
                        }
                        #endregion

                        #region IGNORE: Create required number of parallel GL Token Account creation Tasks
                        //List<Task<IDLTAddress?>> tasks = new List<Task<IDLTAddress?>>();
                        //// Loop through coaTemplate to determine number of postable accounts
                        //int coaTemplatePostableAccountsCount = 0;
                        //foreach (var glCode in coaTemplate.ChartOfAccounts)
                        //{
                        //    // Lookup the properties for the glCode in the General Ledger Accounts Catalog
                        //    var glaccprop = glAccountCatalog[glCode];
                        //    if (glaccprop != null)
                        //    {
                        //        // Only create DLT Account Addresess for "postable" GL accounts
                        //        if ((GLAccountType)glaccprop.Type == GLAccountType.POSTABLE_GROUP_ACCOUNT)
                        //        {
                        //            tasks.Add(_hederaDLTContext.CreateTokenAccountAsync(dltPayorAddress, glToken, glTreasuryAccount, glTokenGrantKycKeys));
                        //            coaTemplatePostableAccountsCount++;
                        //        }
                        //    }
                        //    else
                        //    {
                        //        throw new InvalidOperationException($"Unknown general ledger account code {glCode}.");
                        //    }
                        //}
                        //// Loop through coaMemberTemplate to determine number of postable accounts
                        //int coaMemberTemplatePostableAccountsCount = 0;
                        //foreach (var glCode in coaMemberTemplate.ChartOfAccounts)
                        //{
                        //    // Lookup the properties for the glCode in the General Ledger Accounts Catalog
                        //    var glaccprop = glAccountCatalog[glCode];
                        //    if (glaccprop != null)
                        //    {
                        //        // Only create DLT Account Addresess for "postable" GL accounts
                        //        if ((GLAccountType)glaccprop.Type == GLAccountType.POSTABLE_GROUP_ACCOUNT)
                        //        {
                        //            tasks.Add(_hederaDLTContext.CreateTokenAccountAsync(dltPayorAddress, glToken, glTreasuryAccount, glTokenGrantKycKeys));
                        //            coaMemberTemplatePostableAccountsCount++;
                        //        }
                        //    }
                        //    else
                        //    {
                        //        throw new InvalidOperationException($"Unknown general ledger account code {glCode}.");
                        //    }
                        //}
                        //// Now wait on all accounts to be created
                        //var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                        #endregion

                        #region Generate required Jurisdiction and World Computer General Ledger Service GL Token Accounts 
                        //List<Task<IDLTAddress?>> tasks = new List<Task<IDLTAddress?>>();
                        //List<GLAccountCode> codes = new List<GLAccountCode>();
                        List<DLTGeneralLedgerAccountInfo>? jurisdictionDebitChartOfAccounts = new List<DLTGeneralLedgerAccountInfo>();
                        List<DLTGeneralLedgerAccountInfo>? jurisdictionCreditChartOfAccounts = new List<DLTGeneralLedgerAccountInfo>();
                        List<DLTGeneralLedgerAccountInfo>? wcoGeneralLedgerServiceDebitChartOfAccounts = new List<DLTGeneralLedgerAccountInfo>();
                        List<DLTGeneralLedgerAccountInfo>? wcoGeneralLedgerServiceCreditChartOfAccounts = new List<DLTGeneralLedgerAccountInfo>();
                        await GeneratePlaceHolderGeneralLedgerFromCOAAsync(coaJurisdictionTemplate, 
                                            jurisdictionDebitChartOfAccounts, jurisdictionCreditChartOfAccounts ).ConfigureAwait(false);
                        await GeneratePlaceHolderGeneralLedgerFromCOAAsync(coaWcoGeneralLedgerServiceTemplate, 
                                            wcoGeneralLedgerServiceDebitChartOfAccounts, wcoGeneralLedgerServiceCreditChartOfAccounts).ConfigureAwait(false);
                        #endregion

                        #region Now create the Jurisdiction General Ledger place holder
                        jurisdictionGeneralLedger = new GeneralLedger
                        {
                            ID = wcId,
                            Description = $"WorldComputer",
                            DebitChartOfAccounts = jurisdictionDebitChartOfAccounts,
                            CreditChartOfAccounts = jurisdictionCreditChartOfAccounts,
                            COATemplateID = coaJurisdictionTemplate.ID
                        };
                        #endregion

                        #region Now create the WCO General Ledger Service General Ledger place holder
                        wcoGeneralLedgerServiceGeneralLedger = new GeneralLedger
                        {
                            ID = wcoId,
                            Description = "WCO (WorldComputer.org)",
                            DebitChartOfAccounts = wcoGeneralLedgerServiceDebitChartOfAccounts,
                            CreditChartOfAccounts = wcoGeneralLedgerServiceCreditChartOfAccounts,
                            COATemplateID = coaWcoGeneralLedgerServiceTemplate.ID
                        };
                        #endregion

                        #region IGNORE Generate required Test Member GL Token Accounts 
                        //List<DLTGeneralLedgerAccountInfo>? testMember1DebitChartOfAccounts = new List<DLTGeneralLedgerAccountInfo>();
                        //List<DLTGeneralLedgerAccountInfo>? testMember1CreditChartOfAccounts = new List<DLTGeneralLedgerAccountInfo>();
                        //List<DLTGeneralLedgerAccountInfo>? testMember2DebitChartOfAccounts = new List<DLTGeneralLedgerAccountInfo>();
                        //List<DLTGeneralLedgerAccountInfo>? testMember2CreditChartOfAccounts = new List<DLTGeneralLedgerAccountInfo>();
                        //foreach (var glCode in coaMemberTemplate.ChartOfAccounts)
                        //{
                        //    // Lookup the properties for the glCode in the General Ledger Accounts Catalog
                        //    var glaccprop = glCatalog[glCode];
                        //    if (glaccprop != null)
                        //    {
                        //        // Only create DLT Account Addresess for "postable" GL accounts
                        //        if ((GLAccountType)glaccprop.Type == GLAccountType.POSTABLE_GROUP_ACCOUNT)
                        //        {
                        //            #region Generate a DLT Account Address to represent the postable Test Member 1 debit GL account
                        //            var testMember1DebitAccount = await _hederaDLTContext.CreateTokenAccountAsync(dltPayorAddress, glToken, glTreasuryAccount /*, glTokenGrantKycKeys*/).ConfigureAwait(false);
                        //            if (testMember1DebitAccount == null || testMember1DebitAccount.Status != "Success")
                        //            {
                        //                throw new InvalidOperationException($"Unable to create Test Member 1 debit DLT account address for GL account {glCode}");
                        //            }
                        //            testMember1DebitChartOfAccounts.Add(new DLTGeneralLedgerAccountInfo(glCode, testMember1DebitAccount.AddressID!));
                        //            #endregion
                        //            #region Generate a DLT Account Address to represent the postable Test Member 1 credit GL account
                        //            var testMember1CreditAccount = await _hederaDLTContext.CreateTokenAccountAsync(dltPayorAddress, glToken, glTreasuryAccount /*, glTokenGrantKycKeys */).ConfigureAwait(false);
                        //            if (testMember1CreditAccount == null || testMember1CreditAccount.Status != "Success")
                        //            {
                        //                throw new InvalidOperationException($"Unable to create Test Member 1 credit DLT account address for GL account {glCode}");
                        //            }
                        //            testMember1CreditChartOfAccounts.Add(new DLTGeneralLedgerAccountInfo(glCode, testMember1CreditAccount.AddressID!));
                        //            #endregion
                        //            #region Generate a DLT Account Address to represent the postable Test Member 2 debit GL account
                        //            var testMember2DebitAccount = await _hederaDLTContext.CreateTokenAccountAsync(dltPayorAddress, glToken, glTreasuryAccount /*, glTokenGrantKycKeys */).ConfigureAwait(false);
                        //            if (testMember2DebitAccount == null || testMember2DebitAccount.Status != "Success")
                        //            {
                        //                throw new InvalidOperationException($"Unable to create Test Member 2 debit DLT account address for GL account {glCode}");
                        //            }
                        //            testMember2DebitChartOfAccounts.Add(new DLTGeneralLedgerAccountInfo(glCode, testMember2DebitAccount.AddressID!));
                        //            #endregion
                        //            #region Generate a DLT Account Address to represent the postable Test Member 2 credit GL account
                        //            var testMember2CreditAccount = await _hederaDLTContext.CreateTokenAccountAsync(dltPayorAddress, glToken, glTreasuryAccount /*, glTokenGrantKycKeys */).ConfigureAwait(false);
                        //            if (testMember2CreditAccount == null || testMember2CreditAccount.Status != "Success")
                        //            {
                        //                throw new InvalidOperationException($"Unable to create Test Member 2 credit DLT account address for GL account {glCode}");
                        //            }
                        //            testMember2CreditChartOfAccounts.Add(new DLTGeneralLedgerAccountInfo(glCode, testMember2CreditAccount.AddressID!));
                        //            #endregion
                        //        }
                        //    }
                        //    else
                        //    {
                        //        throw new InvalidOperationException($"Unknown GL Account {glCode}.");
                        //    }
                        //}
                        #region IGNORE Now create the Test Member 1 General Ledger
                        //var testMember1GeneralLedgerID = Guid.NewGuid().ToString();
                        //testMember1GeneralLedger = new GeneralLedger
                        //{
                        //    ID = testMember1GeneralLedgerID,
                        //    Description = $"{testMember1ID} - General Ledger",
                        //    DebitChartOfAccounts = testMember1DebitChartOfAccounts,
                        //    CreditChartOfAccounts = testMember1CreditChartOfAccounts,
                        //    COATemplateID = coaMemberTemplate.ID
                        //};
                        #endregion
                        #region IGNORE Create Test Member 1
                        //var testMember1 = new JurisdictionMember
                        //{
                        //    ID = testMember1ID,
                        //    GeneralLedgerID = testMember1GeneralLedgerID,
                        //    CryptoAddress = testMember1CryptoAddress
                        //};
                        #endregion
                        #region IGNORE Now create the Test Member 2 General Ledger
                        //var testMember2GeneralLedgerID = Guid.NewGuid().ToString();
                        //testMember2GeneralLedger = new GeneralLedger
                        //{
                        //    ID = testMember2GeneralLedgerID,
                        //    Description = $"{testMember2ID} - General Ledger",
                        //    DebitChartOfAccounts = testMember2DebitChartOfAccounts,
                        //    CreditChartOfAccounts = testMember2CreditChartOfAccounts,
                        //    COATemplateID = coaMemberTemplate.ID
                        //};
                        #region IGNORE Create Test Member 2
                        //var testMember2 = new JurisdictionMember
                        //{
                        //    ID = testMember2ID,
                        //    GeneralLedgerID = testMember2GeneralLedgerID,
                        //    CryptoAddress = testMember2CryptoAddress
                        //};
                        #endregion
                        #endregion
                        //}
                        #endregion
                        Debug.Print("About to create contract....");
                        #region Create DAGL Smart Contract Instance
                        var contractTrxReceipt = await _hederaDLTContext.CreateContractAsync().ConfigureAwait(false);
                        if (contractTrxReceipt == null || contractTrxReceipt.Status != "Success")
                        {
                            throw new InvalidOperationException("Unable to create World Computer General Ledger smart contract.");
                        }
                        Debug.Print("Contract successfully created!");
                        #endregion

                        #region Now create the Instance Manifest for this Jurisdiction
                        generalLedgerInstanceManifest = new GeneralLedgerInstanceManifest
                        {
                            WCOGeneralLedgerServiceID = wcoGeneralLedgerServiceGeneralLedger.ID,
                            JurisdictionID = jurisdictionGeneralLedger.ID,
                            WCOGeneralLedgerServiceCOAsTemplateID = wcoGeneralLedgerServiceGeneralLedger.COATemplateID,
                            JurisdictionCOAsTemplateID = jurisdictionGeneralLedger.COATemplateID,
                            JurisdictionMemberCOAsTemplateID = coaMemberTemplate.ID,
                            JurisdictionGeneralLedger = jurisdictionGeneralLedger,
                            WCOGeneralLedgerServiceGeneralLedger = wcoGeneralLedgerServiceGeneralLedger,
                            JurisdictionPayorAddress = null,
                            WCOGeneralLedgerServiceAddress = null, //_glProperties.WCOGeneralLedgerServiceCryptoAddress,
                            JurisdictionTokenAdminKeyVault = glTokenAdminKeys,
                            JurisdictionTokenTreasuryAddress = glTreasuryAccount,
                            JurisdictionToken = glToken,
                            JursidictionJournalEntryPostContractAddress = contractTrxReceipt.ContractId,
                            JurisdictionDefaultAccountngCurrency = new DLTCurrency(_glProperties!.JurisdictionBaseCurrencySymbol, _glProperties!.JurisdictionBaseCurrencyDecimals),
                            JurisdictionServiceBuyerTransactionFeeSchedule = new FeeSchedule(_glProperties!.JurisdictionBuyerTransactionFeeRate, _glProperties!.JurisdictionBuyerTransactionFeeMinimum, _glProperties!.JurisdictionBuyerTransactionFeeMaximum),
                            JurisdictionServiceSellerTransactionFeeSchedule = new FeeSchedule(_glProperties!.JurisdictionSellerTransactionFeeRate, _glProperties!.JurisdictionSellerTransactionFeeMinimum, _glProperties!.JurisdictionSellerTransactionFeeMaximum),
                            JurisdictionServiceMemberCashInFeeSchedule = new FeeSchedule(_glProperties!.JurisdictionMemberCashInFeeRate, _glProperties!.JurisdictionMemberCashInFeeMinimum, _glProperties!.JurisdictionMemberCashInFeeMaximum),
                            JurisdictionServiceMemberCashOutFeeSchedule = new FeeSchedule(_glProperties!.JurisdictionMemberCashOutFeeRate, _glProperties!.JurisdictionMemberCashOutFeeMinimum, _glProperties!.JurisdictionMemberCashOutFeeMaximum),
                            JurisdictionServiceCashOutFeeSchedule = new FeeSchedule(_glProperties!.JurisdictionCashOutFeeRate, _glProperties!.JurisdictionCashOutFeeMinimum, _glProperties!.JurisdictionCashOutFeeMaximum)
                            //TestMember1ID = testMember1ID,
                            //TestMember2ID = testMember2ID
                        };
                        // Create the new jurisdiction instance manifest content for the current persistence provider
                        //await persistenceProvider.CreateJurisdictionInstanceManifestAsync(generalLedgerInstanceManifest).ConfigureAwait(false);
                        #endregion

                        #region IGNORE: Check the crypto balance on the Treasury account to ensure its zero
                        //cryptoBalance = await _hederaDLTContext.GetCryptoAccountBalanceAsync(glTreasuryAccount).ConfigureAwait(false);
                        //if (cryptoBalance.HasValue)
                        //{
                        //    Debug.Print($"Crypto Balance of GL Treasury Account after Token creation {glTreasuryAccount.AddressID} = {cryptoBalance}.");
                        //}
                        //else
                        //{
                        //    Debug.Print($"Could not obtain Crypto Balance of GL Treasury Account after Token creation {glTreasuryAccount.AddressID}.");
                        //}
                        #endregion

                        #region Persist necessary instance data
                        // If we make it here we have everything we need to persist the generated instance to the provided persistence provider
                        //
                        //// First persist the Jurisdiction General Ledger
                        
                        
                        
                        //await persistenceProvider.WriteGeneralLedgerAsync(jurisdictionGeneralLedger).ConfigureAwait(false);
                        //// Next persist the WCO General Ledger Service General ledger
                        //await persistenceProvider.WriteGeneralLedgerAsync(wcoGeneralLedgerServiceGeneralLedger).ConfigureAwait(false);

                        //Next create a special placeholder for a "member" representing the Jurisdiction itself
                        //JurisdictionMember jurisdictionItSelf = new JurisdictionMember
                        //{
                        //    ID = generalLedgerInstanceManifest.JurisdictionID!,
                        //    GeneralLedger = jurisdictionGeneralLedger,
                        //    CryptoAddress = generalLedgerInstanceManifest.JurisdictionPayorAddress
                        //};
                        ////await persistenceProvider.WriteMemberAsync(jurisdictionItSelf).ConfigureAwait(false);

                        // Next create a special placeholder for a "memnber" representing the WCO General Ledger Service itself
                        //JurisdictionMember wcoGeneralLedgerServiceItSelf = new JurisdictionMember
                        //{
                        //    ID = generalLedgerInstanceManifest.WCOGeneralLedgerServiceID!,
                        //    GeneralLedger = wcoGeneralLedgerServiceGeneralLedger,
                        //    CryptoAddress = generalLedgerInstanceManifest.WCOGeneralLedgerServiceAddress
                        //};
                        //await persistenceProvider.WriteMemberAsync(wcoGeneralLedgerServiceItSelf).ConfigureAwait(false);

                        //// Next create a special placeholder for a "journal" representing the Jurisdiction itself
                        //persistenceProvider.CreateJournal(jurisdictionGeneralLedger.ID!);
                        //// Next create a special placeholder for a "journal" representing the WCO General Ledger Service itself
                        //persistenceProvider.CreateJournal(wcoGeneralLedgerServiceGeneralLedger.ID!);
                        //// Next create Test Members #1 and #2
                        ////await persistenceProvider.WriteMemberAsync(testMember1).ConfigureAwait(false);
                        ////await persistenceProvider.WriteMemberAsync(testMember2).ConfigureAwait(false);
                        //// Next create the General Ledgers for Test Members #1 and #2
                        ////await persistenceProvider.WriteGeneralLedgerAsync(testMember1GeneralLedger).ConfigureAwait(false);
                        ////await persistenceProvider.WriteGeneralLedgerAsync(testMember2GeneralLedger).ConfigureAwait(false);
                        //// Next create a special placeholder for "journals" representing each Test Member
                        ////persistenceProvider.CreateJournal(testMember1GeneralLedger.ID!);
                        ////persistenceProvider.CreateJournal(testMember2GeneralLedger.ID!);
                        //// Finally create a placeholder for "triple-entry journals" representing the Jurisdictino Audit
                        //persistenceProvider.CreateJournal(generalLedgerInstanceManifest.JurisdictionID!);
                        #endregion


                        #region Get the crypto account balance of the payor after everything as been created
                        var cryptoPayorAfter = await _hederaDLTContext.GetCryptoAccountBalanceAsync(/*jurisdictionPayorAddress*/).ConfigureAwait(false);
                        if (cryptoPayorAfter.HasValue)
                        {
                            Debug.Print($"Crypto Balance of GL Payor Account AFter  = {cryptoPayorAfter}.");
                            Debug.Print($"Cost of Transaction in tinyHBar={cryptoPayorBefore - cryptoPayorAfter}");
                        }
                        else
                        {
                            Debug.Print($"Could not obtain Crypto Balance of GL Payor Account.");
                        }
                        #endregion
                    }
                    else
                    {
                        throw new InvalidOperationException("Error attempting to create treasury token.");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Error creating treasury account.");
                }
                #endregion 
            }
            catch (Exception ex)
            {
                Debug.Print($"Error in dAccountingService.GenerateInstanceManifestAsync() - {ex}");
                throw;
            }
            return await Task.FromResult(generalLedgerInstanceManifest);
        }



        public async Task GeneratePlaceHolderGeneralLedgerFromCOAAsync(IChartOfAccountsTemplate coaTemplate, List<DLTGeneralLedgerAccountInfo> debitChartOfAccounts, 
                                List<DLTGeneralLedgerAccountInfo> creditChartOfAccounts )
        {
            foreach (var glCode in coaTemplate!.ChartOfAccounts!)
            {
                // Lookup the properties for the glCode in the General Ledger Accounts Catalog
                var glaccprop = glCatalog[glCode];
                if (glaccprop != null)
                {
                    // Only create DLT Account Addresess for "postable" GL accounts
                    if ((GLAccountType)glaccprop.Type == GLAccountType.POSTABLE_GROUP_ACCOUNT)
                    {

                        #region Generate a DLT Account Address to represent the postable debit GL account
                        var jurisdictionDebitAccount = await _hederaDLTContext.CreatePlaceHolderTokenAccountAsync().ConfigureAwait(false);
                        if (jurisdictionDebitAccount == null )
                        {
                            throw new InvalidOperationException($"Unable to create debit DLT account address for GL account {glCode}");
                        }
                        debitChartOfAccounts.Add(new DLTGeneralLedgerAccountInfo(glCode, jurisdictionDebitAccount.AddressID!));
                        #endregion

                        #region Generate a DLT Account Address to represent the postable  credit GL account
                        var jurisdictionCreditAccount = await _hederaDLTContext.CreatePlaceHolderTokenAccountAsync().ConfigureAwait(false);
                        if (jurisdictionCreditAccount == null )
                        {
                            throw new InvalidOperationException($"Unable to create credit DLT account address for GL account {glCode}");
                        }
                        creditChartOfAccounts.Add(new DLTGeneralLedgerAccountInfo(glCode, jurisdictionCreditAccount.AddressID!));
                        #endregion
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Unknown GL Account {glCode}.");
                }
            }
        }

        public bool IsDAGLInitialized()
        {
            return (generalLedgerInstanceManifest != null && !string.IsNullOrWhiteSpace(generalLedgerInstanceManifest.JurisdictionID));
        }

        public IGeneralLedgerInstanceManifest GeneralLedgerInstanceManifest { get { return generalLedgerInstanceManifest; } }

        public List<IChartOfAccountsTemplate> CoaTemplateList { get { return coaTemplateList; } }


        public IGeneralLedgerAccountsCatalog GeneralLedgerAccountsCatalog { get { return _hederaDLTContext.GeneralLedgerAccountsCatalog; } }

        public async Task<IJurisdictionMember> CreateJurisdictionMemeberGeneralLedgerAsync(string? jurisdictionmemberid, string? memberDescription, string? coaTemplateID, IDLTAddress membercryptoaddress = null!)
        {
            IJurisdictionMember jurisdictionMember = null!;
            //int cleanupRequiredOnError = 0;

            try
            {
                #region Validate parameters
                if (string.IsNullOrEmpty(jurisdictionmemberid))
                {
                    throw new ArgumentException("Missing Member ID.");
                }
                if (membercryptoaddress == null)
                {
                    membercryptoaddress = new DLTAddress();
                }
                // We should NOT find a file (i.e.; we should get an exception in the line below)
                //try
                //{
                //    await persistenceProvider.ReadMemberAsync(jurisdictionmemberid).ConfigureAwait(false);
                //    return null!; // indicates jurisdictionmemberid has already been registered 
                //}
                //catch (Exception)
                //{
                //    // An exception is expected (i.e.; member hasn't be registered before)...so ignore and continue
                //}
                #endregion 

                // Generate a GL for the member
                var memberGL = await GenerateJurisdictionMemberGeneralLedgerAsync(jurisdictionmemberid, memberDescription, coaTemplateID ).ConfigureAwait(false);

                if (memberGL != null)
                {
                    // If we make it here we have everything to create the JurisdictionMember to be returned from this method
                    jurisdictionMember = new JurisdictionMember(jurisdictionmemberid, memberGL,  (DLTAddress)membercryptoaddress);

                    //cleanupRequiredOnError = 1;  // any error from here down will require cleanup
                    // Persist the jurisdiction memeber
                    //await persistenceProvider.WriteMemberAsync(jurisdictionMember).ConfigureAwait(false);
                    //cleanupRequiredOnError++;
                    // Persist the GL of the jursidication member
                    //await persistenceProvider.WriteGeneralLedgerAsync(memberGL).ConfigureAwait(false);
                    //cleanupRequiredOnError++;
                    // Next create a special placeholder for "journals" representing the Member
                    //persistenceProvider.CreateJournal(memberGL.ID!);
                }
                else
                {
                    throw new InvalidOperationException("Error generating Member General Ledger");
                }
            }
            catch (Exception ex)
            {
                //if (cleanupRequiredOnError > 0)
                //{
                //    // %TODO% - attempt to remove the DLT Token accounts that were created so as not to be charged for them later at renewal
                //}
                Debug.Print($"Error in GeneralLedgerManager.RegisterJurisdictionMember() - {ex}");
                throw;
            }
            return jurisdictionMember;
        }

        public ulong NetPurchaseAmountIncludingFees(ulong grossAmount)
        {
            #region Determine Fees
            var jurisdictionServiceBuyerFee = 0UL;
            var useFixedJurisdictionServiceBuyerFee = (generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.FeeRate <= 0);

            if (useFixedJurisdictionServiceBuyerFee)
            {
                // If we are using a fixed fee - min and max must be the same
                if (generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MinimumFee != generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MaximumFee)
                {
                    throw new ArgumentException("For fixed member buyer rate - min fee must equal max fee.");
                }
                jurisdictionServiceBuyerFee = generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MinimumFee;
            }
            else
            {
                jurisdictionServiceBuyerFee = Math.Min(Math.Max(generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MinimumFee,
                                                Convert.ToUInt64(grossAmount * generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.FeeRate)),
                                                generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MaximumFee);
            }
            #endregion
            return grossAmount + jurisdictionServiceBuyerFee;
        }


        public ulong NetMemberCashOutAmountIncludingFees(ulong grossAmount)
        {
            #region Determine Fees
            var jurisdictionServiceMemberCashOutFee = 0UL;
            var useFixedJurisdictionMemberServiceCashOutFee = (generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.FeeRate <= 0);

            if (useFixedJurisdictionMemberServiceCashOutFee)
            {
                // If we are using a fixed fee - min and max must be the same
                if (generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.MinimumFee != generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.MaximumFee)
                {
                    throw new ArgumentException("For fixed member cash out rate - min fee must equal max fee.");
                }
                jurisdictionServiceMemberCashOutFee = generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.MinimumFee;
            }
            else
            {
                jurisdictionServiceMemberCashOutFee = Math.Min(Math.Max(generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.MinimumFee,
                                                Convert.ToUInt64(grossAmount * generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.FeeRate)),
                                                generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.MaximumFee);
            }
            #endregion
            return grossAmount + jurisdictionServiceMemberCashOutFee;
        }


        public ulong NetOperatorCashOutAmountIncludingFees(ulong grossAmount)
        {
            #region Determine Fees
            var jurisdictionServiceCashOutFee = 0UL;
            var useFixedJurisdictionServiceCashOutFee = (generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.FeeRate <= 0);

            if (useFixedJurisdictionServiceCashOutFee)
            {
                // If we are using a fixed fee - min and max must be the same
                if (generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.MinimumFee != generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.MaximumFee)
                {
                    throw new ArgumentException("For fixed member cash out rate - min fee must equal max fee.");
                }
                jurisdictionServiceCashOutFee = generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.MinimumFee;
            }
            else
            {
                jurisdictionServiceCashOutFee = Math.Min(Math.Max(generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.MinimumFee,
                                                Convert.ToUInt64(grossAmount * generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.FeeRate)),
                                                generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.MaximumFee);
            }
            #endregion
            return grossAmount + jurisdictionServiceCashOutFee;
        }

        public async Task<long> GetJurisdictionMemberWalletBalanceAsync(string jurisdictionmemberid)
        {
            var jurisdictionMemberGL = await GetMemberGeneralLedgerAsync(jurisdictionmemberid).ConfigureAwait(false);
            IDLTGeneralLedgerAccount walletDebitAccount = new DLTGeneralLedgerAccount
            {
                DLTGeneralLedgerAccountInfo = GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionMemberGL.GeneralLedger?.DebitChartOfAccounts!, GLAccountCode.BANK)
            };
            IDLTGeneralLedgerAccount walletCreditAccount = new DLTGeneralLedgerAccount
            {
                DLTGeneralLedgerAccountInfo = GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionMemberGL.GeneralLedger?.CreditChartOfAccounts!, GLAccountCode.BANK)
            };
            return await GetBankAccountBalanceAsync(walletDebitAccount, walletCreditAccount).ConfigureAwait(false);
        }

        public async Task<ulong> GetJurisdictionDltBalanceAsync()
        {
            ulong? balance = 0;
            try
            {
                balance = await _hederaDLTContext.GetCryptoAccountBalanceAsync(generalLedgerInstanceManifest?.JurisdictionPayorAddress!).ConfigureAwait(false);
            }
            catch (Exception)
            {

                throw;
            }
            return (ulong)balance;
        }



        public async Task<string> GetReport( IJurisdictionMember member, GeneralLedgerReportType reportType, GeneralLedgerReportOptions reportOptions,
                                                DateTime fromUtcDate, DateTime toUtcDate )
        {
            string result = "";
            var glMember = new JurisdictionMemberGeneralLedger(member.GeneralLedger, member);
            switch ( reportType )
            {
                case GeneralLedgerReportType.ChartOfAccounts:
                    result += GetMemberChartOfAccountsAsync( glMember );
                    break;
                case GeneralLedgerReportType.TrailBalance:
                    var tbReport = await GetMemberGeneralLedgerReportsAsync( glMember, reportOptions, null, toUtcDate ).ConfigureAwait(false);
                    result += tbReport.DumpTrialBalance();
                    break;
                case GeneralLedgerReportType.IncomeStatement:
                    var isReport = await GetMemberGeneralLedgerReportsAsync(glMember, reportOptions, fromUtcDate, toUtcDate).ConfigureAwait(false);
                    result += isReport.IncomeStatementReport();
                    break;
                case GeneralLedgerReportType.BalanceSheet:
                    var bsReport = await GetMemberGeneralLedgerReportsAsync(glMember, reportOptions, fromUtcDate, toUtcDate).ConfigureAwait(false);
                    result += bsReport.BalanceSheetReport();
                    break;
                case GeneralLedgerReportType.GeneralLedger:
                //case GeneralLedgerReportType.GeneralLedgerAudit:
                    JurisdictionMember jurisdictionMemberItSelf2 = new JurisdictionMember
                    {
                        ID = generalLedgerInstanceManifest.JurisdictionID!,
                        GeneralLedger = _generalLedgerContext.WorldComputerGeneralLedger,
                        CryptoAddress = generalLedgerInstanceManifest.JurisdictionPayorAddress
                    };
                    var journalEntries2 = await ReadAllJournalEntryRecordsAsync(jurisdictionMemberItSelf2);
                    //bool isFullAudit = glMember.GeneralLedger!.ID!.Equals(_generalLedgerContext.WorldComputerGeneralLedger.ID!, StringComparison.OrdinalIgnoreCase);
                    result += JournalEntryAccounts.GenerateJournalReport(
                                                                    journalEntries2,
                                                                    glMember.JurisdictionMember!,
                                                                    _hederaDLTContext.GeneralLedgerInstanceManifest.CultureInfo!,
                                                                    (DLTCurrency)_hederaDLTContext.GeneralLedgerInstanceManifest.JurisdictionDefaultAccountngCurrency!,
                                                                    glCatalog,
                                                                    _generalLedgerContext.WorldComputerGeneralLedger.ID!,
                                                                    "",  // Reserved for future use
                                                                    false //(reportType == GeneralLedgerReportType.GeneralLedgerAudit)
                                                                    );
                    break;
                case GeneralLedgerReportType.JournalEntries:
                case GeneralLedgerReportType.JournalEntriesAudit:
                    JurisdictionMember jurisdictionMemberItSelf = new JurisdictionMember
                    {
                        ID = generalLedgerInstanceManifest.JurisdictionID!,
                        GeneralLedger = _generalLedgerContext.WorldComputerGeneralLedger,
                        CryptoAddress = generalLedgerInstanceManifest.JurisdictionPayorAddress
                    };
                    var journalEntries = await ReadAllJournalEntryRecordsAsync(jurisdictionMemberItSelf);
                    //bool isFullAudit = glMember.GeneralLedger!.ID!.Equals(_generalLedgerContext.WorldComputerGeneralLedger.ID!, StringComparison.OrdinalIgnoreCase);
                    result += JournalEntryAccounts.GenerateJournalEntriesReport(
                                                                    journalEntries,
                                                                    glMember.JurisdictionMember!,
                                                                    _hederaDLTContext.GeneralLedgerInstanceManifest.CultureInfo!,
                                                                    (DLTCurrency)_hederaDLTContext.GeneralLedgerInstanceManifest.JurisdictionDefaultAccountngCurrency!,
                                                                    glCatalog,
                                                                    _generalLedgerContext.WorldComputerGeneralLedger.ID!, 
                                                                    "",  // Reserved for future use
                                                                    (reportType == GeneralLedgerReportType.JournalEntriesAudit)
                                                                    );
                    break;
                
                default:
                    result += "**** Unknown ReportType ***";
                    break;
            }
            return result;
        }
        

        public async Task<List<IJournalEntryRecord>> GetMemberJournalEntriesAsync(IJurisdictionMemberGeneralLedger jurisdictionMemberGeneralLedger)
        {
            List<IJournalEntryRecord> journalEntryRecordList = null!;
            //try
            //{
            //    #region Validate parameters
            //    if (jurisdictionMemberGeneralLedger == null || jurisdictionMemberGeneralLedger.GeneralLedger == null ||
            //                jurisdictionMemberGeneralLedger.GeneralLedger?.DebitChartOfAccounts == null || jurisdictionMemberGeneralLedger.GeneralLedger?.CreditChartOfAccounts == null)
            //    {
            //        throw new ArgumentException("Invalid General Ledger.");
            //    }
            //    #endregion

            //    #region Read the journal entries for the passed it member GL from the persistence layer
            //    journalEntryRecordList = await persistenceProvider.ReadJournalAsync(jurisdictionMemberGeneralLedger.GeneralLedger.ID!, GetMemberGeneralLedgerAsync);
            //    #endregion

            //}
            //catch (Exception)
            //{

            //    throw;
            //}
            return await Task.FromResult(journalEntryRecordList);
        }

      
        public async Task<IDLTTransactionConfirmation?> PostSynchronousMemberPurchaseAsync(  IJurisdictionMember buyerMember,
                                                                                            IJurisdictionMember sellerMember,
                                                                                            ulong? amount, string purchaseDescription )
        {
            IDLTTransactionConfirmation? transactionConfirmation = null!;
            JournalEntryAccounts je = null!;
            try
            {
                #region Validate arguments
                if (amount == 0)
                {
                    throw new ArgumentException("Purchase Amount must be greater than zero.");
                }
                #endregion

                #region Explanation
                //  A synchronous Member Purchase is the simplest kind of transaction.  It synchronously (i.e.; atomically) 
                //  transfers money from a buyer's wallet to a seller's wallet.  This kind of transaction is suitable when there is no
                //  delay in providing product/service for payment.  I.e.; it can be done immediatley.  This is often the case for digital goods.
                //  Because a synchronous Member Purchase transaction can release payment at the same time as the product/service is delivered there is no need to create the
                //  extra journal entries that typically accompany an asyncrhonous transaction which would involve two separate rounds of journal entries -
                //  one involving the posting to the Accounts Payable and Accounts Receivable of the buyer and seller respectively at the time the "order" is placed,
                //  (i.e.; classic Purchase Order and Invoice semantics), and the second one occuring later (after the product or service has been delivered) to handle the
                //  actual payment for the product/service entailing a post from/to the Wallet accounts of the buyer/seller respectively.
                //
                //  Journal entries are triple-entries spanning 3 separate general ledgers:  the buyer's, the seller's, and the jurisdiction's (i.e.; the platform). 
                //  Each of these 3 separate general ledgers are atomically posted to with a single journal entry, effectively synchronousing and validating the
                //  transactions across all 3 parties.  
                //
                //  The tri-part journal entry must balance their debits and credits i) within each stakeholder's individual GL journal entry and ii) across all parts of the journal entry.
                //
                //  The journal entry for the default synchronous Member Purchase is computed as follows:
                //
                //  PARTY           |   ACCOUNT                             |    DB                                                                           |       CR
                //  ==========================================================================================================================================================================
                //  Buyer           |   WALLET (Asset)                      |                                                                                 |   <amount> + <jurisdiction_buyer_fee> 
                //  Buyer           |   MISCELLANEOUS_PURCHASES (Expense)   |   <amount>                                                                      |
                //  Buyer           |   JURISDICTION_TRX_FEES  (Expense)    |   <jurisdiction_buyer_fee>                                                      |
                //  --------------------------------------------------------------------------------------------------------------------------------------------------------------------------
                //  Seller          |   WALLET  (Asset)                     |   <amount> - <jurisdiction_seller_fee> - <blockio-fee>                          |
                //  Seller          |   MISCELLANEOUS_SALES  (Revenue)      |                                                                                 |   <amount>
                //  Seller          |   CONTENT_DISTRIBUTION  (Expense)     |   <jurisdiction_blockio_fee>                                                    |
                //  Seller          |   JURISDICTION_TRX_FEES (Expense)     |   <jurisdiction_seller_fee>                                                     |
                //  --------------------------------------------------------------------------------------------------------------------------------------------------------------------------
                //  Jurisdiction    |   WALLET  (Asset)                     |   <jurisdiction_buyer_fee>+<jurisdiction_seller_fee>+<jurisdiction_blockio_fee> |
                //  Jurisdiction    |   EARNED_TRX_FEES (Revenue)           |                                                                                 |   <jurisdiction_buyer_fee>+<jurisdiction_seller_fee>                      
                //  Jurisdiction    |   CONTENT_DISTRIBUTION_ESCROW (Revenue)                                                                                 |   <jurisdiction_blockio_fee>   
                //  --------------------------------------------------------------------------------------------------------------------------------------------------------------------------
                //
                //
                // For example, if a Buyer purchases a digital good (say a photo) from a Seller for $10, and the Jursidiction buyer and seller fees are both 10%, 
                // then the journal entry would look like:
                // 
                //  PARTY           |   ACCOUNT                             |    DB                                                 |       CR
                //  ========================================================================================================================================================
                //  Buyer           |   WALLET (Asset)                      |                                                       |   $11.00 
                //  Buyer           |   MISCELLANEOUS_PURCHASES (Expense)   |   $10.00                                              |
                //  Buyer           |   JURISDICTION_TRX_FEES  (Expense)    |   $ 1.00                                              |
                //  --------------------------------------------------------------------------------------------------------------------------------------------------------
                //  Seller          |   WALLET  (Asset)                     |   $ 8.00                                              |
                //  Seller          |   MISCELLANEOUS_SALES  (Revenue)      |                                                       |   $10.00
                //  Seller          |   CONTENT_DISTRIBUTION (Expense)      |   $ 1.00                                              |
                //  Seller          |   JURISDICTION_TRX_FEES (Expense)     |   $ 1.00                                              |
                //  --------------------------------------------------------------------------------------------------------------------------------------------------------
                //  Jurisdiction    |   WALLET  (Asset)                     |   $ 3.00                                              |
                //  Jurisdiction    |   EARNED_TRX_FEES (Revenue)           |                                                       |   $ 2.00 
                //  Jurisdiction    |   CONTENT_DISTRIBUTION_ESCROW (Revenue|                                                       |   $ 1.00
                //  --------------------------------------------------------------------------------------------------------------------------------------------------------
                //
                //  Notice how i) each party's section of the overall journal entry balances its debits and credits in the specific content of that party, and 
                //  ii) the overall journal entry also balances its debits and credits
                #endregion

                #region Determine Buyer's and Seller's DLT General Ledger Chart of Accounts
                //var buyerMember = await GetMemberGeneralLedgerAsync(buyerMemberId).ConfigureAwait(false);
                //var sellerMember = await GetMemberGeneralLedgerAsync(sellerMemberId).ConfigureAwait(false);
                //var jurisdictionMember = await GetMemberGeneralLedgerAsync(generalLedgerInstanceManifest?.JurisdictionID!).ConfigureAwait(false);                
                JurisdictionMember jurisdictionMemberItSelf = new JurisdictionMember
                {
                    ID = generalLedgerInstanceManifest.JurisdictionID!,
                    GeneralLedger = _generalLedgerContext.WorldComputerGeneralLedger,
                    CryptoAddress = generalLedgerInstanceManifest.JurisdictionPayorAddress
                };
                var jurisdictionMember = new JurisdictionMemberGeneralLedger(jurisdictionMemberItSelf.GeneralLedger, jurisdictionMemberItSelf);
                var buyerDebitCOAs = buyerMember.GeneralLedger?.DebitChartOfAccounts;
                var buyerCreditCOAs = buyerMember.GeneralLedger?.CreditChartOfAccounts;
                var sellerDebitCOAs = sellerMember.GeneralLedger?.DebitChartOfAccounts;
                var sellerCreditCOAs = sellerMember.GeneralLedger?.CreditChartOfAccounts;
                #endregion 

                #region Determine Fees
                var jurisdictionBuyerFee = 0UL;
                var jurisdictionzSellerFee = 0UL;
                var useFixedJurisdictionServiceBuyerFee = (generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.FeeRate <= 0);
                var useFixedJurisdictionServiceSellerFee = (generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.FeeRate <= 0);

                if (useFixedJurisdictionServiceBuyerFee)
                {
                    // If we are using a fixed fee - min and max must be the same
                    if (generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MinimumFee != generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MaximumFee)
                    {
                        throw new InvalidOperationException("For fixed jurisdiction buyer transaction rate - min fee must equal max fee.");
                    }
                    jurisdictionBuyerFee = generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MinimumFee;
                }
                else
                {
                    jurisdictionBuyerFee = Math.Min(Math.Max(generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MinimumFee,
                                                    Convert.ToUInt64(amount * generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.FeeRate)),
                                                    generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MaximumFee);
                }

                if (useFixedJurisdictionServiceSellerFee)
                {
                    // If we are using a fixed fee - min and max must be the same
                    if (generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.MinimumFee != generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.MaximumFee)
                    {
                        throw new InvalidOperationException("For fixed jurisdiction seller transaction rate - min fee must equal max fee.");
                    }
                    jurisdictionzSellerFee = generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.MinimumFee;
                }
                else
                {
                    jurisdictionzSellerFee = Math.Min(Math.Max(generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.MinimumFee,
                                                    Convert.ToUInt64(amount * generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.FeeRate)),
                                                    generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.MaximumFee);
                }
                #endregion

                #region Determine Content Distribution Fees
                var jurisdicationSellerContentDistributionRate = 0.10m;  // %TODO - for now
                var jurisdicationSellerContentDistributionFee = Convert.ToUInt64(amount * jurisdicationSellerContentDistributionRate);
                #endregion

                #region Verify Buyer has enough funds in Wallet to make purchase
                var buyerDebitWalletAccount = new DLTGeneralLedgerAccount(buyerMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(buyerDebitCOAs!, GLAccountCode.BANK));
                var buyerCreditWalletAccount = new DLTGeneralLedgerAccount(sellerMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(buyerCreditCOAs!, GLAccountCode.BANK));
                var buyerWalletAccountBalance = await GetBankAccountBalanceAsync(buyerDebitWalletAccount, buyerCreditWalletAccount).ConfigureAwait(false);  // Returns a negative number if balance is a Credit
                if (buyerWalletAccountBalance < Convert.ToInt64(amount + jurisdictionBuyerFee) )
                {
                    throw new DLTInsufficientBankFunds();  // %TODO% - For now...
                }
                #endregion

                #region Prepare the complete Journal Entry to be posted to the DLT
                je = new JournalEntryAccounts(/*GLSParameters*/);
                var buyerDebits = new List<IDLTGeneralLedgerAccount>();
                var buyerCredits = new List<IDLTGeneralLedgerAccount>();
                var sellerDebits = new List<IDLTGeneralLedgerAccount>();
                var sellerCredits = new List<IDLTGeneralLedgerAccount>();
                var jurisdictionDebits = new List<IDLTGeneralLedgerAccount>();
                var jurisdictionCredits = new List<IDLTGeneralLedgerAccount>();
                // Create the Buyer's Debit(s) portion of the journal entry
                buyerDebits.Add(new DLTGeneralLedgerAccount(buyerMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(buyerDebitCOAs!, GLAccountCode.GENERAL_PURCHASES), amount));
                buyerDebits.Add(new DLTGeneralLedgerAccount(buyerMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(buyerDebitCOAs!, GLAccountCode.WORLDCOMPUTER_TRX_FEES), jurisdictionBuyerFee));

                // Create the Buyer's Credit(s) portion of the journal entry
                buyerCredits.Add(new DLTGeneralLedgerAccount(buyerMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(buyerCreditCOAs!, GLAccountCode.BANK), amount + jurisdictionBuyerFee));

                // Create the Seller's Debit(s) portion of the journal entry
                sellerDebits.Add(new DLTGeneralLedgerAccount(sellerMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(sellerDebitCOAs!, GLAccountCode.BANK), amount - jurisdictionzSellerFee - jurisdicationSellerContentDistributionFee));
                sellerDebits.Add(new DLTGeneralLedgerAccount(sellerMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(sellerDebitCOAs!, GLAccountCode.WORLDCOMPUTER_TRX_FEES), jurisdictionzSellerFee));
                sellerDebits.Add(new DLTGeneralLedgerAccount(sellerMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(sellerDebitCOAs!, GLAccountCode.EXP_CONTENT_DISTRIBUTION), jurisdicationSellerContentDistributionFee));

                // Create the Seller's Credit(s) portion of the journal entry
                sellerCredits.Add(new DLTGeneralLedgerAccount(sellerMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(sellerCreditCOAs!, GLAccountCode.GENERAL_SALES), amount));

                // Create the Jurisdiction's Debit(s) portion of the journal entry
                jurisdictionDebits.Add(new DLTGeneralLedgerAccount( jurisdictionMember.JurisdictionMember!,
                                                            GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(_generalLedgerContext!.WorldComputerGeneralLedger!.DebitChartOfAccounts!, GLAccountCode.BANK),
                                                            jurisdictionBuyerFee + jurisdictionzSellerFee + jurisdicationSellerContentDistributionFee));

                // Create the Jurisdiction's Credit(s) portion of the journal entry
                jurisdictionCredits.Add(new DLTGeneralLedgerAccount(jurisdictionMember.JurisdictionMember!,
                                                            GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(_generalLedgerContext!.WorldComputerGeneralLedger!.CreditChartOfAccounts!, GLAccountCode.EARNED_TRX_FEES),
                                                            jurisdictionBuyerFee + jurisdictionzSellerFee + jurisdicationSellerContentDistributionFee));
                //jurisdictionCredits.Add(new DLTGeneralLedgerAccount(jurisdictionMember.JurisdictionMember!,
                  //                                          GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(_generalLedgerContext!.WorldComputerGeneralLedger!.CreditChartOfAccounts!, GLAccountCode.CONTENT_DISTRIBUTION_ESCROW),
                    //                                        jurisdicationSellerContentDistributionFee));

                // Finally create the complete Journal Entry by adding all of the stakeholder's debit and credit portions
                je.AddDoubleEntry( buyerDebits, buyerCredits);
                je.AddDoubleEntry( sellerDebits, sellerCredits);
                je.AddDoubleEntry( jurisdictionDebits, jurisdictionCredits);
                string memo = "Content Access";
                if( !string.IsNullOrEmpty(purchaseDescription))
                {
                    memo += $" - {purchaseDescription}";
                }
                JournalEntryRecord journalEntryRecord = new JournalEntryRecord
                {
                    TransactionID = Guid.NewGuid().ToString(),
                    Memo = memo,
                    IsAutoReversal = false,
                    //PostDate = DateTime.UtcNow,
                    PostDate = timeManager.ProcessorUtcTime,
                    JournalEntry = je
                };
                #endregion

                #region Post Journal Entry to DLT
                //trxConfirm = await _hederaDLTContext.SmartContractJournalEntryTokenTransferAsync(  journalEntryRecord,
                //                                                                                isReversing: false
                //                                                                            ).ConfigureAwait(false);

                //                                                                        journalEntryRecord,
                //                                                                        isReversing: true,
                //                                                                        null!,                                // ignored: no exteranl crypto account involved in a reset
                //                                                                        transferOutToExternalAccount: false,  // ignored: no exteranl crypto account involved in a reset
                //                                                                        0,
                //                                                                        clearaccounts: true
                transactionConfirmation = await _hederaDLTContext.JournalEntryTokenTransferAsync(journalEntryRecord,
                                                                                null!,                                  // ignore: no external crypto account involved in Member-to-Member transactions
                                                                                transferOutToExternalAccount: false,    // ignore: no external crypto account involved in Member-to-Member transactions
                                                                                0,                                      // ignore: no external crypto account involved in Member-to-Member transactions    
                                                                                null!                                   // ignore: no external crypto account signatory in Member-to-Member transactions
                                                                                ).ConfigureAwait(false);
                if (transactionConfirmation != null)
                {
                    Guid journalEntryBlobId = await _securityContext.ProcessDLTTransactionConfirmationAsync(_hederaDLTContext.GeneralLedgerProperties, transactionConfirmation).ConfigureAwait(false);
                    await SendGlobalGLJournalEntryEventAsync(journalEntryBlobId).ConfigureAwait(false);
                }
                #endregion 
            }
            catch(DLTInsufficientBankFunds)  // %TODO% --- For now....
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.Print($"Error in dGeneralLedgerServiceController.PostSynchronousMemberPurchaseAsync() - {ex}");
                throw;
            }
            return transactionConfirmation;
        }


        public async Task<IDLTTransactionConfirmation?> PostFundsTransferAsync(IJurisdictionMember senderMember,
                                                                               IJurisdictionMember receiverMember,
                                                                               ulong? amount,
                                                                               GeneralLedgerUnitOfAmountType unitOfAmount )
        {
            IDLTTransactionConfirmation? transactionConfirmation = null!;
            JournalEntryAccounts je = null!;
            try
            {
                ulong? originalAmount = amount;
                #region Validate arguments
                if (amount == 0)
                {
                    throw new ArgumentException("Purchase Amount must be greater than zero.");
                }
                #endregion

                #region Normalize Amount
                decimal? usdExchangeRate = null!;
                switch( unitOfAmount)
                {
                    case GeneralLedgerUnitOfAmountType.TINYBAR:
                        // NOP - amount already normalized
                        break;
                    case GeneralLedgerUnitOfAmountType.HBAR:
                        amount = amount * 100_000_000UL;
                        break;
                    case GeneralLedgerUnitOfAmountType.USD:
                        usdExchangeRate = await _hederaDLTContext.GetCurrentHBarUSDExchangeRateAsync().ConfigureAwait(false);
                        amount = Convert.ToUInt64(amount / usdExchangeRate * 100_000_000);
                        break;
                    default:
                        throw new ArgumentException("Unkonwn Unit of Amount.");
                }
                #endregion 

                #region Explanation
                //  A synchronous Member Purchase is the simplest kind of transaction.  It synchronously (i.e.; atomically) 
                //  transfers money from a buyer's wallet to a seller's wallet.  
                //
                //  Journal entries are triple-entries spanning 3 separate general ledgers:  the sender's, the receiver's, and the jurisdiction's (i.e.; the WorldComputer). 
                //  Each of these 3 separate general ledgers are atomically posted to with a single journal entry, effectively synchronousing and validating the
                //  transactions across all 3 parties.  
                //
                //  The tri-part journal entry must balance their debits and credits i) within each stakeholder's individual GL journal entry and ii) across all parts of the journal entry.
                //
                //  The journal entry for the default synchronous Member Purchase is computed as follows:
                //
                //  PARTY           |   ACCOUNT                             |    DB                                                 |     CR
                //  ==========================================================================================================================================================================
                //  Sender          |   Bank (Asset)                        |                                                       |   <amount> + <jurisdiction_sender_fee> 
                //  Sender          |   Intra Funds Transfers Out (Expense) |  <amount>                                             |
                //  Sender          |   WorldComputer Trx Fees  (Expense)   |  <jurisdiction_sender_fee>                            |
                //  --------------------------------------------------------------------------------------------------------------------------------------------------------------------------
                //  Receiver        |   Bank (Asset)                        |  <amount> - <jurisdiction_receiver_fee>               |
                //  Receiver        |   Intra Funds Transfers In (Revenue)  |                                                       |   <amount>
                //  Receiver        |   WorldComputer Trx Fees (Expense)    |  <jurisdiction_seller_fee>                            |
                //  --------------------------------------------------------------------------------------------------------------------------------------------------------------------------
                //  World Computer  |   Bank  (Asset)                       |  <jurisdiction_sender_fee>+<jurisdiction_receiver_fee>|
                //  World Computer  |   Earned Trx Fees (Revenue)           |                                                       |   <jurisdiction_sender_fee>+<jurisdiction_receiver_fee>                      
                //  --------------------------------------------------------------------------------------------------------------------------------------------------------------------------
                //
                //
                // For example, if a Buyer purchases a digital good (say a photo) from a Seller for $10, and the Jursidiction buyer and seller fees are both 10%, 
                // then the journal entry would look like:
                // 
                //  PARTY           |   ACCOUNT                             |    DB                                                 |     CR
                //  ========================================================================================================================================================
                //  Sender          |   Bank (Asset)                        |                                                       |   $11.00 
                //  Sender          |   Intra Funds Transfers Out (Expense) |   $10.00                                              |
                //  Sender          |   WorldComputer Trx Fees  (Expense)   |   $ 1.00                                              |
                //  --------------------------------------------------------------------------------------------------------------------------------------------------------
                //  Receiver        |   Bank  (Asset)                       |   $ 9.00                                              |
                //  Receiver        |   Intra Funds Transfers In (Revenue)  |                                                       |   $10.00
                //  Receiver        |   WorldComputer Trx Fees (Expense)    |   $ 1.00                                              |
                //  --------------------------------------------------------------------------------------------------------------------------------------------------------
                //  World Computer  |   Bank (Asset)                        |   $ 2.00                                              |
                //  World Computer  |   Earned Trx Fees (Revenue)           |                                                       |   $ 2.00 
                //  --------------------------------------------------------------------------------------------------------------------------------------------------------
                //
                //  Notice how i) each party's section of the overall journal entry balances its debits and credits in the specific content of that party, and 
                //  ii) the overall journal entry also balances its debits and credits
                #endregion

                #region Determine Sender's and Receivers's DLT General Ledger Chart of Accounts
                JurisdictionMember jurisdictionMemberItSelf = new JurisdictionMember
                {
                    ID = generalLedgerInstanceManifest.JurisdictionID!,
                    GeneralLedger = _generalLedgerContext.WorldComputerGeneralLedger,
                    CryptoAddress = generalLedgerInstanceManifest.JurisdictionPayorAddress
                };
                var jurisdictionMember = new JurisdictionMemberGeneralLedger(jurisdictionMemberItSelf.GeneralLedger, jurisdictionMemberItSelf);
                var senderDebitCOAs = senderMember.GeneralLedger?.DebitChartOfAccounts;
                var senderCreditCOAs = senderMember.GeneralLedger?.CreditChartOfAccounts;
                var receiverDebitCOAs = receiverMember.GeneralLedger?.DebitChartOfAccounts;
                var receiverCreditCOAs = receiverMember.GeneralLedger?.CreditChartOfAccounts;
                #endregion 

                #region Determine Fees
                var jurisdictionReceiverFee = 0UL;
                var jurisdictionSenderFee = 0UL;
                var useFixedJurisdictionServiceReceiverFee = (generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.FeeRate <= 0);
                var useFixedJurisdictionServiceSenderFee = (generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.FeeRate <= 0);

                if (useFixedJurisdictionServiceReceiverFee)
                {
                    // If we are using a fixed fee - min and max must be the same - NOTE:  Receiver == Buyer in this context
                    if (generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MinimumFee != generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MaximumFee)
                    {
                        throw new InvalidOperationException("For fixed jurisdiction buyer transaction rate - min fee must equal max fee.");
                    }
                    jurisdictionReceiverFee = generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MinimumFee;
                }
                else
                {
                    jurisdictionReceiverFee = Math.Min(Math.Max(generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MinimumFee,
                                                    Convert.ToUInt64(amount * generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.FeeRate)),
                                                    generalLedgerInstanceManifest!.JurisdictionServiceBuyerTransactionFeeSchedule!.MaximumFee);
                }

                if (useFixedJurisdictionServiceSenderFee)
                {
                    // If we are using a fixed fee - min and max must be the same - NOTE:  Sender = Seller in this context
                    if (generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.MinimumFee != generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.MaximumFee)
                    {
                        throw new InvalidOperationException("For fixed jurisdiction seller transaction rate - min fee must equal max fee.");
                    }
                    jurisdictionSenderFee = generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.MinimumFee;
                }
                else
                {
                    jurisdictionSenderFee = Math.Min(Math.Max(generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.MinimumFee,
                                                    Convert.ToUInt64(amount * generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.FeeRate)),
                                                    generalLedgerInstanceManifest!.JurisdictionServiceSellerTransactionFeeSchedule!.MaximumFee);
                }
                #endregion

                #region Verify Sender has enough funds in Wallet to make purchase
                var senderDebitWalletAccount = new DLTGeneralLedgerAccount(senderMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(senderDebitCOAs!, GLAccountCode.BANK));
                var senderCreditWalletAccount = new DLTGeneralLedgerAccount(senderMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(senderCreditCOAs!, GLAccountCode.BANK));
                var senderWalletAccountBalance = await GetBankAccountBalanceAsync(senderDebitWalletAccount, senderCreditWalletAccount).ConfigureAwait(false);  // Returns a negative number if balance is a Credit
                if (senderWalletAccountBalance < Convert.ToInt64(amount + jurisdictionSenderFee))
                {
                    throw new DLTInsufficientBankFunds();  // %TODO% - For now...
                }
                #endregion

                #region Prepare the complete Journal Entry to be posted to the DLT
                je = new JournalEntryAccounts(/*GLSParameters*/);
                var receiverDebits = new List<IDLTGeneralLedgerAccount>();
                var receiverCredits = new List<IDLTGeneralLedgerAccount>();
                var senderDebits = new List<IDLTGeneralLedgerAccount>();
                var senderCredits = new List<IDLTGeneralLedgerAccount>();
                var jurisdictionDebits = new List<IDLTGeneralLedgerAccount>();
                var jurisdictionCredits = new List<IDLTGeneralLedgerAccount>();
                // Create the Receiver's Debit(s) portion of the journal entry
                receiverDebits.Add(new DLTGeneralLedgerAccount(receiverMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(receiverDebitCOAs!, GLAccountCode.BANK), amount - jurisdictionReceiverFee));
                receiverDebits.Add(new DLTGeneralLedgerAccount(receiverMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(receiverDebitCOAs!, GLAccountCode.WORLDCOMPUTER_TRX_FEES), jurisdictionReceiverFee));

                // Create the Receiver's Credit(s) portion of the journal entry
                receiverCredits.Add(new DLTGeneralLedgerAccount(receiverMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(receiverCreditCOAs!, GLAccountCode.INTRA_FUNDS_TRANSFERS_IN), amount ));

                // Create the Sender's Debit(s) portion of the journal entry
                senderDebits.Add(new DLTGeneralLedgerAccount(senderMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(senderDebitCOAs!, GLAccountCode.INTRA_FUNDS_TRANSFERS_OUT), amount ));
                senderDebits.Add(new DLTGeneralLedgerAccount(senderMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(senderDebitCOAs!, GLAccountCode.WORLDCOMPUTER_TRX_FEES), jurisdictionSenderFee));

                // Create the Sender's Credit(s) portion of the journal entry
                senderCredits.Add(new DLTGeneralLedgerAccount(senderMember, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(senderCreditCOAs!, GLAccountCode.BANK), amount + jurisdictionSenderFee));

                // Create the Jurisdiction's Debit(s) portion of the journal entry
                jurisdictionDebits.Add(new DLTGeneralLedgerAccount(jurisdictionMember.JurisdictionMember!,
                                                            GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(_generalLedgerContext!.WorldComputerGeneralLedger!.DebitChartOfAccounts!, GLAccountCode.BANK),
                                                            jurisdictionReceiverFee + jurisdictionSenderFee));

                // Create the Jurisdiction's Credit(s) portion of the journal entry
                jurisdictionCredits.Add(new DLTGeneralLedgerAccount(jurisdictionMember.JurisdictionMember!,
                                                            GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(_generalLedgerContext!.WorldComputerGeneralLedger!.CreditChartOfAccounts!, GLAccountCode.EARNED_TRX_FEES),
                                                            jurisdictionReceiverFee + jurisdictionSenderFee));

                // Finally create the complete Journal Entry by adding all of the stakeholder's debit and credit portions
                je.AddDoubleEntry(receiverDebits, receiverCredits);
                je.AddDoubleEntry(senderDebits, senderCredits);
                je.AddDoubleEntry(jurisdictionDebits, jurisdictionCredits);
                string memo = $"Funds Transfer to {receiverMember.GeneralLedger!.Description!.ToUpper()}";
                if(unitOfAmount == GeneralLedgerUnitOfAmountType.USD)
                {
                    memo += $" - (${originalAmount} @ {usdExchangeRate} per HBar)";
                }
                JournalEntryRecord journalEntryRecord = new JournalEntryRecord
                {
                    TransactionID = Guid.NewGuid().ToString(),
                    Memo = memo,
                    IsAutoReversal = false,
                    PostDate = timeManager.ProcessorUtcTime,
                    JournalEntry = je
                };
                #endregion

                #region Post Journal Entry to DLT
                transactionConfirmation = await _hederaDLTContext.JournalEntryTokenTransferAsync(journalEntryRecord,
                                                                                null!,                                  // ignore: no external crypto account involved in Member-to-Member transactions
                                                                                transferOutToExternalAccount: false,    // ignore: no external crypto account involved in Member-to-Member transactions
                                                                                0,                                      // ignore: no external crypto account involved in Member-to-Member transactions    
                                                                                null!                                   // ignore: no external crypto account signatory in Member-to-Member transactions
                                                                                ).ConfigureAwait(false);
                if (transactionConfirmation != null)
                {
                    Guid journalEntryBlobId = await _securityContext.ProcessDLTTransactionConfirmationAsync(_hederaDLTContext.GeneralLedgerProperties, transactionConfirmation).ConfigureAwait(false);
                    await SendGlobalGLJournalEntryEventAsync(journalEntryBlobId).ConfigureAwait(false);
                }
                #endregion 
            }
            catch (DLTInsufficientBankFunds)  // %TODO% --- For now....
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.Print($"Error in GeneralLedgerManager.PostFundsTransferAsync() - {ex}");
                throw;
            }
            return transactionConfirmation;
        }



        public async Task<IDLTTransactionConfirmation?> FundMemberWalletAsync(IJurisdictionMember member, string dltAddress, string dltPrivateKey, ulong fundsAmount,
                                                                                GeneralLedgerUnitOfAmountType unitOfAmount)
        {           
            var glMember = new JurisdictionMemberGeneralLedger(member.GeneralLedger, member);
            IKeyVault keyVault = _hederaDLTContext.LoadKeys(null!, dltPrivateKey);
            IDLTTransactionConfirmation? transactionConfirmation = await CashInMemberWalletAsync(glMember, new DLTAddress(dltAddress,keyVault),
                                            fundsAmount, unitOfAmount ).ConfigureAwait(false);
            if (transactionConfirmation != null)
            {
                Guid journalEntryBlobId = await _securityContext.ProcessDLTTransactionConfirmationAsync(_hederaDLTContext.GeneralLedgerProperties, transactionConfirmation).ConfigureAwait(false);
                await SendGlobalGLJournalEntryEventAsync(journalEntryBlobId).ConfigureAwait(false);
            }
            return transactionConfirmation;
        }


        public async Task<IDLTTransactionConfirmation?> DefundMemberWalletAsync(IJurisdictionMember member, string dltAddress, string dltPrivateKey, ulong fundsAmount,
                                                                                GeneralLedgerUnitOfAmountType unitOfAmount)
        {
            var glMember = new JurisdictionMemberGeneralLedger(member.GeneralLedger, member);
            IKeyVault keyVault = _hederaDLTContext.LoadKeys(null!, dltPrivateKey);
            IDLTTransactionConfirmation? transactionConfirmation = await CashOutMemberWalletAsync(glMember, new DLTAddress(dltAddress, keyVault),
                                            fundsAmount, unitOfAmount).ConfigureAwait(false);
            if (transactionConfirmation != null)
            {
                Guid journalEntryBlobId = await _securityContext.ProcessDLTTransactionConfirmationAsync(_hederaDLTContext.GeneralLedgerProperties, transactionConfirmation).ConfigureAwait(false);
                await SendGlobalGLJournalEntryEventAsync(journalEntryBlobId).ConfigureAwait(false);

            }
            return transactionConfirmation;
        }

       

        public async Task<IDLTTransactionConfirmation?> ResetAsync( string buyerMemberId, string sellerMemberId )
        {
            IDLTTransactionConfirmation trxConfirm = null!;
            IJournalEntryAccounts je = null!;
            try
            {
                #region Validate arguments
                #endregion

                #region Fetch Trail Balances for all stakeholders
                var Buyer = await GetMemberGeneralLedgerAsync(buyerMemberId).ConfigureAwait(false);
                var buyerTrialBalance = await GetMemberGeneralLedgerReportsAsync(Buyer, GeneralLedgerReportOptions.TextOutput ).ConfigureAwait(false);
                if (buyerTrialBalance == null)
                {
                    throw new InvalidOperationException("Unable to retrieve trial balnce for Buyer GL");
                }
                var Seller = await GetMemberGeneralLedgerAsync(sellerMemberId).ConfigureAwait(false);
                var sellerTrialBalance = await GetMemberGeneralLedgerReportsAsync(Seller, GeneralLedgerReportOptions.TextOutput).ConfigureAwait(false);
                if (sellerTrialBalance == null)
                {
                    throw new InvalidOperationException("Unable to retrieve trial balnce for Seller GL");
                }
                var jurisdictionTrialBalance = await GetMemberGeneralLedgerReportsAsync(
                                                        await GetMemberGeneralLedgerAsync(generalLedgerInstanceManifest?.JurisdictionID!).ConfigureAwait(false),
                                                        GeneralLedgerReportOptions.TextOutput).ConfigureAwait(false);
                if (jurisdictionTrialBalance == null)
                {
                    throw new InvalidOperationException("Unable to retrieve trial balnce for Jurisdiction GL");
                }
                var WCOAccountingServiceTrialBalance = await GetWCOAccountingServiceTrialBalanceAsync(GeneralLedgerReportOptions.TextOutput).ConfigureAwait(false);
                if (WCOAccountingServiceTrialBalance == null)
                {
                    throw new InvalidOperationException("Unable to retrieve trial balnce for dAccounting Service GL");
                }
                #endregion

                #region Create debit/credit account lists for each stackholder
                var buyerDebits = new List<IDLTGeneralLedgerAccount>();
                var buyerCredits = new List<IDLTGeneralLedgerAccount>();
                var sellerDebits = new List<IDLTGeneralLedgerAccount>();
                var sellerCredits = new List<IDLTGeneralLedgerAccount>();
                var jurisdictionDebits = new List<IDLTGeneralLedgerAccount>();
                var jurisdictionCredits = new List<IDLTGeneralLedgerAccount>();
                var dAccountingServiceDebits = new List<IDLTGeneralLedgerAccount>();
                var dAccountingServiceCredits = new List<IDLTGeneralLedgerAccount>();

                // Loop through all buyer Debit accounts from the trial balance and if it 
                // has a non-zero balance switch it to a buyer Credit
                foreach (var dbacc in buyerTrialBalance.DebitChartOfAccounts!)
                {
                    if (dbacc.Amount > 0)
                    {
                        buyerDebits.Add(dbacc);
                    }
                }
                // Loop through all buyer Credit accounts from the trial balance and if it 
                // has a non-zero balance switch it to a buyer Debit
                foreach (var cracc in buyerTrialBalance.CreditChartOfAccounts!)
                {
                    if (cracc.Amount > 0)
                    {
                        buyerCredits.Add(cracc);
                    }
                }
                // Loop through all seller Debit accounts from the trial balance and if it 
                // has a non-zero balance switch it to a buyer Credit
                foreach (var dbacc in sellerTrialBalance.DebitChartOfAccounts!)
                {
                    if (dbacc.Amount > 0)
                    {
                        sellerDebits.Add(dbacc);
                    }
                }
                // Loop through all seller Credit accounts from the trial balance and if it 
                // has a non-zero balance switch it to a buyer Debit
                foreach (var cracc in sellerTrialBalance.CreditChartOfAccounts!)
                {
                    if (cracc.Amount > 0)
                    {
                        sellerCredits.Add(cracc);
                    }
                }
                // Loop through all jurisdiction Debit accounts from the trial balance and if it 
                // has a non-zero balance switch it to a buyer Credit
                foreach (var dbacc in jurisdictionTrialBalance.DebitChartOfAccounts!)
                {
                    if (dbacc.Amount > 0)
                    {
                        jurisdictionDebits.Add(dbacc);
                    }
                }
                // Loop through all jurisdiction Credit accounts from the trial balance and if it 
                // has a non-zero balance switch it to a buyer Debit
                foreach (var cracc in jurisdictionTrialBalance.CreditChartOfAccounts!)
                {
                    if (cracc.Amount > 0)
                    {
                        jurisdictionCredits.Add(cracc);
                    }
                }
                // Loop through all dAccounting Service Debit accounts from the trial balance and if it 
                // has a non-zero balance switch it to a buyer Credit
                foreach (var dbacc in WCOAccountingServiceTrialBalance.DebitChartOfAccounts!)
                {
                    if (dbacc.Amount > 0)
                    {
                        dAccountingServiceDebits.Add(dbacc);
                    }
                }
                // Loop through all dAccounting Service Credit accounts from the trial balance and if it 
                // has a non-zero balance switch it to a buyer Debit
                foreach (var cracc in WCOAccountingServiceTrialBalance.CreditChartOfAccounts!)
                {
                    if (cracc.Amount > 0)
                    {
                        dAccountingServiceCredits.Add(cracc);
                    }
                }
                #endregion 

                #region Generate a zeroing Journal Entry from every postable account in the GL
                je = new JournalEntryAccounts(/*GLSParameters*/);
                bool haveEntries = false;
                if (buyerDebits.Count + buyerCredits.Count > 0)
                {
                    haveEntries = true;
                    je.AddDoubleEntry( buyerDebits, buyerCredits, assertBalanced: false);
                }
                if (sellerDebits.Count + sellerCredits.Count > 0)
                {
                    haveEntries = true;
                    je.AddDoubleEntry( sellerDebits, sellerCredits, assertBalanced: false);
                }
                if (jurisdictionDebits.Count + jurisdictionCredits.Count > 0)
                {
                    haveEntries = true;
                    je.AddDoubleEntry( jurisdictionDebits, jurisdictionCredits, assertBalanced: false);
                }
                if (dAccountingServiceDebits.Count + dAccountingServiceCredits.Count > 0)
                {
                    haveEntries = true;
                    je.AddDoubleEntry( dAccountingServiceDebits, dAccountingServiceCredits, assertBalanced: false);
                }
                JournalEntryRecord journalEntryRecord = new JournalEntryRecord
                {
                    TransactionID = Guid.NewGuid().ToString(),
                    Memo = "Zero accounts",
                    IsAutoReversal = false,
                    //PostDate = DateTime.UtcNow,
                    PostDate = timeManager.ProcessorUtcTime,
                    JournalEntry = je
                };
                #endregion

                #region Post Journal Entry to DLT
                //if (haveEntries)
                //{
                //    var trxReceipt = await _hederaDLTContext.JournalEntryTokenTransferAsync(  //generalLedgerInstanceManifest!.JurisdictionPayorAddress!,
                //                                                                        //generalLedgerInstanceManifest!.JurisdictionToken!,
                //                                                                        //generalLedgerInstanceManifest!.JurisdictionTokenTreasuryAddress!,
                //                                                                        journalEntryRecord,
                //                                                                        isReversing: true,
                //                                                                        null!,                                // ignored: no exteranl crypto account involved in a reset
                //                                                                        transferOutToExternalAccount: false,  // ignored: no exteranl crypto account involved in a reset
                //                                                                        0,
                //                                                                        clearaccounts: true
                //                                                                     ).ConfigureAwait(false);
                //    if (trxReceipt == null)
                //    {
                //        throw new InvalidOperationException("Failed to complte Reset journal entry token transfer for unknown reason.");
                //    }
                //    else
                //    {
                //        if (trxReceipt.Status != "Success")
                //        {
                //            throw new InvalidOperationException($"Failed to complte journal entry token transfer - Status = {trxReceipt.Status}.");
                //        }
                //        else
                //        {

                //            trxConfirm = new DLTTransactionConfirmation(trxReceipt.Status, trxReceipt.TransactionId);
                //        }
                //    }
                //}
                //else
                //{
                //    // There were no non-zero amounts to reverse so simply report success
                //    trxConfirm = new DLTTransactionConfirmation("Success", null!);
                //}
                if (haveEntries)
                {
                    var trxReceipt = await _hederaDLTContext.SmartContractJournalEntryTokenTransferAsync( journalEntryRecord,
                                                                                                    isReversing: true,
                                                                                                    clearaccounts: true
                                                                                                    ).ConfigureAwait(false);
                    if (trxReceipt == null)
                    {
                        throw new InvalidOperationException("Failed to complte Reset journal entry token transfer for unknown reason.");
                    }
                    else
                    {
                        if (trxReceipt.Status != "Success")
                        {
                            throw new InvalidOperationException($"Failed to complte journal entry token transfer - Status = {trxReceipt.Status}.");
                        }
                        else
                        {

                            trxConfirm = new DLTTransactionConfirmation(trxReceipt.Status, trxReceipt.TransactionId);
                        }
                    }
                }
                // NOTE:  Finally reset the journals for each stakeholder general ledger  as well as the Jurisdiction "audit" journal
                List<string> gllist = new List<string>();
                foreach (var jerentry in journalEntryRecord.JournalEntry.DebtAccountList)
                {
                    if (!gllist.Contains(jerentry.JurisdictionMember!.GeneralLedger!.ID!))
                    {
                        gllist.Add(jerentry.JurisdictionMember.GeneralLedger!.ID!);
                    }
                }
                foreach (var jerentry in journalEntryRecord.JournalEntry.CreditAccountList)
                {
                    if (!gllist.Contains(jerentry.JurisdictionMember!.GeneralLedger!.ID!))
                    {
                        gllist.Add(jerentry.JurisdictionMember.GeneralLedger!.ID!);
                    }
                }
                gllist.Add(_hederaDLTContext.GeneralLedgerProperties.JurisdictionID!);
                foreach (var gl in gllist)
                {
                    persistenceProvider.CreateJournal(gl);
                }
                #endregion 
            }
            catch (Exception ex)
            {
                Debug.Print($"Error in dGeneralLedgerServiceController.ResetAsync() - {ex}");
                throw;
            }
            return trxConfirm;
        }


        public async Task<List<IJournalEntryRecord>> GetJurisdictionAuditAsync()
        {
            await Task.CompletedTask;
            //return await persistenceProvider.ReadJournalAsync(_hederaDLTContext.GeneralLedgerProperties.JurisdictionID!, GetMemberGeneralLedgerAsync);
            return null!;
        }


        public async Task<List<IJournalEntryRecord>> GetJurisdictionJournalEntriesAsync()
        {
            var jurisdictionMember = await GetMemberGeneralLedgerAsync(generalLedgerInstanceManifest!.JurisdictionID!).ConfigureAwait(false);
            return await GetMemberJournalEntriesAsync(jurisdictionMember).ConfigureAwait(false);
        }


        public async Task<List<IJournalEntryRecord>> GetWCOAccountingServiceJournalEntriesAsync()
        {
            var dAccountingServiceMember = await GetMemberGeneralLedgerAsync(generalLedgerInstanceManifest!.WCOGeneralLedgerServiceID!).ConfigureAwait(false);
            return await GetMemberJournalEntriesAsync(dAccountingServiceMember).ConfigureAwait(false);

        }


        // %TODO - Temporary
        public async Task<IDLTTransactionConfirmation?> TransferCryptoAsync( IDLTAddress receivingAccount, long amount)
        {
            IDLTTransactionConfirmation? trxConfirm = null!;
            try
            {
                trxConfirm = await _hederaDLTContext.SimpleCryptoTransferAsync(  // generalLedgerInstanceManifest!.JurisdictionPayorAddress!,  // Payor
                                                                            generalLedgerInstanceManifest!.JurisdictionPayorAddress!,  // Sender
                                                                            receivingAccount,
                                                                            amount
                                                                      ).ConfigureAwait(false);
                if (trxConfirm == null)
                {
                    throw new InvalidOperationException("Error attempting simple crypto transfer.");
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"Error in dGeneralLedgerServiceController.TransferCryptoAsync() - {ex}");
                throw;
            }
            return trxConfirm;
        }

 

        public async Task<IGeneralLedgerReports> GetWCOAccountingServiceTrialBalanceAsync(GeneralLedgerReportOptions reportOptions)
        {
            var dAccountingServiceMember = await GetMemberGeneralLedgerAsync(generalLedgerInstanceManifest!.WCOGeneralLedgerServiceID!).ConfigureAwait(false);
            return await GetMemberGeneralLedgerReportsAsync(dAccountingServiceMember, reportOptions).ConfigureAwait(false);
        }



        //public async Task<IDLTAddress?> CreateCryptoAccountAsync()
        //{
        //    DLTAddress? newAccount = null!;
        //    try
        //    {
        //        newAccount = (DLTAddress?)await _hederaDLTContext.CreateCryptoAccountAsync(/*generalLedgerInstanceManifest!.JurisdictionPayorAddress!*/).ConfigureAwait(false);
        //        if (newAccount == null)
        //        {
        //            throw new InvalidOperationException("Error creating crypto account.");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.Print($"Error in dGeneralLedgerServiceController.CreateCryptoAccountAsync() - {ex}");
        //        throw;
        //    }
        //    return newAccount;
        //}

        public async Task<bool> UnregisterJurisdictionMemeber(IJurisdictionMember member)
        {
            return await Task.FromResult(false).ConfigureAwait(false);
        }


        public async Task<IJurisdictionMemberGeneralLedger> GetMemberGeneralLedgerAsync(string jurisdictionMemberId)
        {
            JurisdictionMemberGeneralLedger jmgl = null!;
            try
            {
                var member = (JurisdictionMember)await persistenceProvider.ReadMemberAsync(jurisdictionMemberId).ConfigureAwait(false);
                if (member != null && !string.IsNullOrEmpty(member.GeneralLedger!.ID))
                {
                    var gl = (GeneralLedger)await persistenceProvider.ReadGeneralLedgerAsync(member.GeneralLedger!.ID!);
                    if (gl == null)
                    {
                        throw new FileNotFoundException();
                    }
                    jmgl = new JurisdictionMemberGeneralLedger
                    {
                        GeneralLedger = gl,
                        JurisdictionMember = new JurisdictionMember
                        {
                            ID = member.ID,
                            CryptoAddress = member.CryptoAddress,
                            GeneralLedger = gl
                        }

                    };
                }
                else
                {
                    throw new FileNotFoundException();
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"Error in GeneralLedgerManager.GetMemberGeneralLedgerAsync() - {ex}");
                throw new FileNotFoundException();
            }
            return jmgl;
        }


        #endregion

        #region Helpers
        #region Event Handlers
        private void On_GL_JOURNAL_ENTRY_POST_Event(Guid sendingPeerNodeDIDRef, IGlobalEvent globalEvent, ulong sequence, long timeStampSeconds, int timeStampNanos)
        {
            try
            {
                var runInBackground = EntryPoint.FlashNode(EntryPoint.FlashConsoleColorEvent);
                if (!EntryPoint.AnimateNodesOnScreen)
                {
                    if (GlobalPropertiesContext.IsSimulatedNode())
                    {
                        _globalPropertiesContext!.WriteToConsole!.DynamicInvoke($"Received ", $"JOURNAL ENTRY POST event from Node #{GlobalPropertiesContext.ComputeNodeNumberFromSimulationNodeID(sendingPeerNodeDIDRef)}", EntryPoint.FlashConsoleColorEvent);
                    }
                    else
                    {
                        _globalPropertiesContext!.WriteToConsole!.DynamicInvoke($"Received ", $"JOURNAL ENTRY POST event from Node:{sendingPeerNodeDIDRef}", EntryPoint.FlashConsoleColorEvent);
                    }
                }
                #region Append the journalEntryBlobId to end of Journal Entries file
                Guid journalEntryBlobId = globalEvent.CorrelationID;
                lock(_journalEntriesFileStream)
                {
                    _journalEntriesFileStream.Seek(0, SeekOrigin.End); 
                    _journalEntriesFileStream.Write(journalEntryBlobId.ToByteArray(),0, 16);
                    _journalEntriesFileStream.Flush();
                }
                #endregion 
            }
            catch (Exception ex)
            {
                Debug.Print($"GeneralLedgerManager.On_GL_JOURNAL_ENTRY_POST_Event(ERROR) {ex}");
            }
        }
        #endregion


        #region Wallet 
        private async Task<long> GetBankAccountBalanceAsync(IDLTGeneralLedgerAccount debitWalletAccount, IDLTGeneralLedgerAccount creditWalletAccount)
        {
            #region Obtain debit & credit balances of Wallet in parallel
            Task<ulong?>[] balanceTasks = new Task<ulong?>[2];
            balanceTasks[0] = _hederaDLTContext.GetTokenAccountBalanceAsync(generalLedgerInstanceManifest!.JurisdictionToken!,
                                                                new DLTAddress(debitWalletAccount.DLTGeneralLedgerAccountInfo!.DLTAddress!));
            balanceTasks[1] = _hederaDLTContext.GetTokenAccountBalanceAsync(generalLedgerInstanceManifest!.JurisdictionToken!,
                                                                new DLTAddress(creditWalletAccount.DLTGeneralLedgerAccountInfo!.DLTAddress!));
            var balances = await Task.WhenAll(balanceTasks).ConfigureAwait(false);
            if (balances[0].HasValue && balances[1].HasValue)
            {
                debitWalletAccount.Amount = balances[0];
                creditWalletAccount.Amount = balances[1];
            }
            else
            {
                throw new InvalidOperationException("Unable to retrieve balance of wallet at this time.");
            }
            #endregion

            #region Compute balance of wallet as a plus or minus number
            long buyerWalletAccountBalance = 0L;
            if (debitWalletAccount.Amount.HasValue)
            {
                if (creditWalletAccount.Amount.HasValue)
                {
                    buyerWalletAccountBalance = Convert.ToInt64(debitWalletAccount.Amount.Value) - Convert.ToInt64(creditWalletAccount.Amount!.Value);

                }
                else
                {
                    buyerWalletAccountBalance = Convert.ToInt64(debitWalletAccount.Amount.Value);
                }
            }
            else
            {
                if (creditWalletAccount.Amount.HasValue)
                {
                    buyerWalletAccountBalance = -1 * Convert.ToInt64(creditWalletAccount.Amount!.Value);
                }
                else
                {
                    // Should never get here
                    throw new ArgumentException("Invalid account balance");
                }
            }
            #endregion 
            return buyerWalletAccountBalance;
        }


        private async Task<IDLTTransactionConfirmation?> CashInMemberWalletAsync(IJurisdictionMemberGeneralLedger jurisdictionMemberGeneralLedgerToFund,
                                                               IDLTAddress sourceCryptoAccount,
                                                               ulong? amount,
                                                               GeneralLedgerUnitOfAmountType unitOfAmount,
                                                               object signatory = null!)
        {
            IDLTTransactionConfirmation? trxConfirm = null!;
            JournalEntryAccounts je = null!;
            try
            {
                ulong? originalAmount = amount;                
                #region Validate arguments
                if (sourceCryptoAccount == null)
                {
                    throw new ArgumentException(nameof(sourceCryptoAccount));
                }
                if (amount == 0)
                {
                    throw new ArgumentException("Purchase Amount must be greater than zero.");
                }
                #endregion
                #region Normalize Amount
                decimal? usdExchangeRate = null!;
                switch (unitOfAmount)
                {
                    case GeneralLedgerUnitOfAmountType.TINYBAR:
                        // NOP - amount already normalized
                        break;
                    case GeneralLedgerUnitOfAmountType.HBAR:
                        amount = amount * 100_000_000UL;
                        break;
                    case GeneralLedgerUnitOfAmountType.USD:
                        usdExchangeRate = await _hederaDLTContext.GetCurrentHBarUSDExchangeRateAsync().ConfigureAwait(false);
                        amount = Convert.ToUInt64(amount / usdExchangeRate * 100_000_000);
                        break;
                    default:
                        throw new ArgumentException("Unkonwn Unit of Amount.");
                }
                #endregion 
                bool fundingOperatorWallet = (jurisdictionMemberGeneralLedgerToFund.JurisdictionMember!.ID!.Equals(generalLedgerInstanceManifest.JurisdictionID, StringComparison.OrdinalIgnoreCase));
                #region Determine the DLT General Ledger Chart of Accounts
                JurisdictionMember jurisdictionMemberItSelf = new JurisdictionMember
                {
                    ID = generalLedgerInstanceManifest.JurisdictionID!,
                    GeneralLedger = _generalLedgerContext.WorldComputerGeneralLedger,
                    CryptoAddress = generalLedgerInstanceManifest.JurisdictionPayorAddress
                };
                var jurisdictionMemberGL = new JurisdictionMemberGeneralLedger(jurisdictionMemberItSelf.GeneralLedger, jurisdictionMemberItSelf);
                var buyerDebitCOAs = jurisdictionMemberGeneralLedgerToFund.GeneralLedger?.DebitChartOfAccounts;
                var buyerCreditCOAs = jurisdictionMemberGeneralLedgerToFund.GeneralLedger?.CreditChartOfAccounts;
                var jurisdictionDebitCOAs = _generalLedgerContext!.WorldComputerGeneralLedger!.DebitChartOfAccounts;
                var jurisdictionCreditCOAs = _generalLedgerContext!.WorldComputerGeneralLedger!.CreditChartOfAccounts;
                #endregion

                #region Determine Fees
                var jurisdictionServiceMemberCashInFee = 0UL;
                var useFixedJurisdictionServiceMemberCashInFee = (generalLedgerInstanceManifest!.JurisdictionServiceMemberCashInFeeSchedule!.FeeRate <= 0);

                if (useFixedJurisdictionServiceMemberCashInFee)
                {
                    // If we are using a fixed fee - min and max must be the same
                    if (generalLedgerInstanceManifest!.JurisdictionServiceMemberCashInFeeSchedule!.MinimumFee != generalLedgerInstanceManifest!.JurisdictionServiceMemberCashInFeeSchedule!.MaximumFee)
                    {
                        throw new ArgumentException("For fixed member cash in rate - min fee must equal max fee.");
                    }
                    jurisdictionServiceMemberCashInFee = generalLedgerInstanceManifest!.JurisdictionServiceMemberCashInFeeSchedule!.MinimumFee;
                }
                else
                {
                    jurisdictionServiceMemberCashInFee = Math.Min(Math.Max(generalLedgerInstanceManifest!.JurisdictionServiceMemberCashInFeeSchedule!.MinimumFee,
                                                    Convert.ToUInt64(amount * generalLedgerInstanceManifest!.JurisdictionServiceMemberCashInFeeSchedule!.FeeRate)),
                                                    generalLedgerInstanceManifest!.JurisdictionServiceMemberCashInFeeSchedule!.MaximumFee);
                }
                #endregion

                #region Construct Journal Entry
                je = new JournalEntryAccounts(/*GLSParameters*/);
                var buyerDebits = new List<IDLTGeneralLedgerAccount>();
                var buyerCredits = new List<IDLTGeneralLedgerAccount>();
                var jurisdictionDebits = new List<IDLTGeneralLedgerAccount>();
                var jurisdictionCredits = new List<IDLTGeneralLedgerAccount>();

                if (!fundingOperatorWallet) // funding a member involves the Member and the Operator (Jurisdiction) stakeholders
                {
                    // Member being funded
                    // Create the member Debit(s) portion of the journal entry
                    buyerDebits.Add(new DLTGeneralLedgerAccount(jurisdictionMemberGeneralLedgerToFund.JurisdictionMember!, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(buyerDebitCOAs!, GLAccountCode.BANK), amount - jurisdictionServiceMemberCashInFee));
                    buyerDebits.Add(new DLTGeneralLedgerAccount(jurisdictionMemberGeneralLedgerToFund.JurisdictionMember!, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(buyerDebitCOAs!, GLAccountCode.WORLDCOMPUTER_TRX_FEES), jurisdictionServiceMemberCashInFee));
                    // Create the member Credit(s) portion of the journal entry
                    buyerCredits.Add(new DLTGeneralLedgerAccount(jurisdictionMemberGeneralLedgerToFund.JurisdictionMember!, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(buyerCreditCOAs!, GLAccountCode.DUE_STAKEHOLDER), amount));

                    // Jurisdiction
                    // Create the jurisdiction Debit(s) portion of the journal entry
                    jurisdictionDebits.Add(new DLTGeneralLedgerAccount(jurisdictionMemberGL.JurisdictionMember!,
                                                                GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionDebitCOAs!, GLAccountCode.BANK),
                                                                jurisdictionServiceMemberCashInFee));
                    // Create the jurisdiction Credit(s) portion of the journal entry
                    jurisdictionCredits.Add(new DLTGeneralLedgerAccount(jurisdictionMemberGL.JurisdictionMember!,
                                                                GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionCreditCOAs!, GLAccountCode.EARNED_TRX_FEES),
                                                                jurisdictionServiceMemberCashInFee));
                    // Finally create the complete Journal Entry by adding all of the stakeholder's debit and credit portions
                    je.AddDoubleEntry(buyerDebits, buyerCredits);
                    je.AddDoubleEntry(jurisdictionDebits, jurisdictionCredits);
                }
                else  // funding Operator does not involve any other stakeholders
                {
                    // Jurisdiction
                    // Create the jurisdiction Debit(s) portion of the journal entry
                    jurisdictionDebits.Add(new DLTGeneralLedgerAccount(jurisdictionMemberGL.JurisdictionMember!,
                                                                GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionDebitCOAs!, GLAccountCode.BANK),
                                                                amount));
                    // Create the jurisdiction Credit(s) portion of the journal entry
                    jurisdictionCredits.Add(new DLTGeneralLedgerAccount(jurisdictionMemberGL.JurisdictionMember!,
                                                                GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionCreditCOAs!, GLAccountCode.DUE_STAKEHOLDER),
                                                                amount));
                    // Finally create the complete Journal Entry by adding all of the stakeholder's debit and credit portions
                    je.AddDoubleEntry(jurisdictionDebits, jurisdictionCredits);
                }
                string memo = $"Bank Cash-In from source Crypto Address: {sourceCryptoAccount.AddressID}";
                if (unitOfAmount == GeneralLedgerUnitOfAmountType.USD)
                {
                    memo += $" - (${originalAmount} @ {usdExchangeRate} per HBar)";
                }
                JournalEntryRecord journalEntryRecord = new JournalEntryRecord
                {
                    TransactionID = Guid.NewGuid().ToString(),
                    Memo = memo,
                    IsAutoReversal = false,
                    //PostDate = DateTime.UtcNow,
                    PostDate = timeManager.ProcessorUtcTime,
                    JournalEntry = je
                };
                #endregion

                #region Post Journal Entry to DLT
                trxConfirm = await _hederaDLTContext.JournalEntryTokenTransferAsync(journalEntryRecord,
                                                                                sourceCryptoAccount,
                                                                                transferOutToExternalAccount: false,  // Funding a wallet requires transfering crypto FROM an extrnal account, so we pass false here
                                                                                Convert.ToInt64(amount),
                                                                                signatory
                                                                                ).ConfigureAwait(false);

                //IJournalEntryRecord memberToFundJournalEntryRecord = (IJournalEntryRecord)new JournalEntryRecord
                //{
                //    Number = 0,
                //    Memo = "Member-to-Member Purchase",
                //    PostDate = DateTime.UtcNow,
                //    JournalEntry = je
                //};
                //await persistenceProvider.AppendJournalAsync(_generalLedgerContext!.WorldComputerGeneralLedger!.ID!, memberToFundJournalEntryRecord).ConfigureAwait(false);
                #endregion 
            }
            catch (Exception ex)
            {
                Debug.Print($"Error in GeneralLedgerManager.CashInMemberWalletAsync() - {ex}");
                throw;
            }
            return trxConfirm;
        }


        private async Task<IDLTTransactionConfirmation?> CashOutMemberWalletAsync(IJurisdictionMemberGeneralLedger memberGeneralLedgerToCashOut,
                                                                                    IDLTAddress destinationCryptoAccount,
                                                                                    ulong? amount, GeneralLedgerUnitOfAmountType unitOfAmount)
        {
            IDLTTransactionConfirmation? trxConfirm = null!;
            JournalEntryAccounts je = null!;
            try
            {
                ulong? originalAmount = amount;
                #region Validate arguments
                if (destinationCryptoAccount == null)
                {
                    throw new ArgumentException(nameof(destinationCryptoAccount));
                }
                if (amount == 0)
                {
                    throw new ArgumentException("Cash-Out amount must be greater than zero.");
                }
                #endregion

                #region Normalize Amount
                decimal? usdExchangeRate = null!;
                switch (unitOfAmount)
                {
                    case GeneralLedgerUnitOfAmountType.TINYBAR:
                        // NOP - amount already normalized
                        break;
                    case GeneralLedgerUnitOfAmountType.HBAR:
                        amount = amount * 100_000_000UL;
                        break;
                    case GeneralLedgerUnitOfAmountType.USD:
                        usdExchangeRate = await _hederaDLTContext.GetCurrentHBarUSDExchangeRateAsync().ConfigureAwait(false);
                        amount = Convert.ToUInt64(amount / usdExchangeRate * 100_000_000);
                        break;
                    default:
                        throw new ArgumentException("Unkonwn Unit of Amount.");
                }
                #endregion 

                #region Determine the DLT General Ledger Chart of Accounts
                JurisdictionMember jurisdictionMemberItSelf = new JurisdictionMember
                {
                    ID = generalLedgerInstanceManifest.JurisdictionID!,
                    GeneralLedger = _generalLedgerContext.WorldComputerGeneralLedger,
                    CryptoAddress = generalLedgerInstanceManifest.JurisdictionPayorAddress
                };
                var jurisdictionMemberGL = new JurisdictionMemberGeneralLedger(jurisdictionMemberItSelf.GeneralLedger, jurisdictionMemberItSelf);
                var memberDebitCOAs = memberGeneralLedgerToCashOut.GeneralLedger?.DebitChartOfAccounts;
                var memberCreditCOAs = memberGeneralLedgerToCashOut.GeneralLedger?.CreditChartOfAccounts;
                var jurisdictionDebitCOAs = _generalLedgerContext!.WorldComputerGeneralLedger!.DebitChartOfAccounts;
                var jurisdictionCreditCOAs = _generalLedgerContext!.WorldComputerGeneralLedger!.CreditChartOfAccounts;
                #endregion

                #region Get Member's Bank balance
                var memberDebitBankAccount = new DLTGeneralLedgerAccount(memberGeneralLedgerToCashOut.JurisdictionMember!, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(memberDebitCOAs!, GLAccountCode.BANK));
                var memberCreditBankAccount = new DLTGeneralLedgerAccount(memberGeneralLedgerToCashOut.JurisdictionMember!, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(memberCreditCOAs!, GLAccountCode.BANK));
                var memberBankAccountBalance = await GetBankAccountBalanceAsync(memberDebitBankAccount, memberCreditBankAccount).ConfigureAwait(false);  // Returns a negative number if balance is a Credit
                #endregion

                #region Check if Cashing out 'All' funds in Bank (i.e.; amount == ulong.MaxValue )
                if (amount == ulong.MaxValue)
                {
                    if (memberBankAccountBalance > 0)
                    {
                        amount = Convert.ToUInt64(memberBankAccountBalance);
                    }
                    else
                    {
                        throw new DLTInsufficientBankFunds();
                    }
                }
                #endregion 


                #region Determine Fees
                var jurisdictionServiceMemberCashOutFee = 0UL;
                var useFixedJurisdictionServiceMemberCashOutFee = (generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.FeeRate <= 0);
                if (useFixedJurisdictionServiceMemberCashOutFee)
                {
                    // If we are using a fixed fee - min and max must be the same
                    if (generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.MinimumFee != generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.MaximumFee)
                    {
                        throw new ArgumentException("For fixed member cash out rate - min fee must equal max fee.");
                    }
                    jurisdictionServiceMemberCashOutFee = generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.MinimumFee;
                }
                else
                {
                    jurisdictionServiceMemberCashOutFee = Math.Min(Math.Max(generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.MinimumFee,
                                                    Convert.ToUInt64(amount * generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.FeeRate)),
                                                    generalLedgerInstanceManifest!.JurisdictionServiceMemberCashOutFeeSchedule!.MaximumFee);
                }
                #endregion

                #region Verify Member has enough funds in Bank to cash out the requested amount
                //var memberDebitBankAccount = new DLTGeneralLedgerAccount(memberGeneralLedgerToCashOut.JurisdictionMember!, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(memberDebitCOAs!, GLAccountCode.BANK));
                //var memberCreditBankAccount = new DLTGeneralLedgerAccount(memberGeneralLedgerToCashOut.JurisdictionMember!, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(memberCreditCOAs!, GLAccountCode.BANK));
                //var memberBankAccountBalance = await GetBankAccountBalanceAsync(memberDebitBankAccount, memberCreditBankAccount).ConfigureAwait(false);  // Returns a negative number if balance is a Credit
                if (Convert.ToUInt64(memberBankAccountBalance) < amount)
                {
                    throw new DLTInsufficientBankFunds();
                }
                #endregion

                #region Construct Journal Entry
                je = new JournalEntryAccounts();
                var sellerDebits = new List<IDLTGeneralLedgerAccount>();
                var sellerCredits = new List<IDLTGeneralLedgerAccount>();
                var jurisdictionDebits = new List<IDLTGeneralLedgerAccount>();
                var jurisdictionCredits = new List<IDLTGeneralLedgerAccount>();

                // Member (Seller) 
                // Create the Member Debit(s) portion of the journal entry
                sellerDebits.Add(new DLTGeneralLedgerAccount(memberGeneralLedgerToCashOut.JurisdictionMember!, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(memberDebitCOAs!, GLAccountCode.DUE_STAKEHOLDER), amount - jurisdictionServiceMemberCashOutFee));
                sellerDebits.Add(new DLTGeneralLedgerAccount(memberGeneralLedgerToCashOut.JurisdictionMember!, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(memberDebitCOAs!, GLAccountCode.WORLDCOMPUTER_TRX_FEES), jurisdictionServiceMemberCashOutFee));
                // Create the Member Credit(s) portion of the journal entry
                sellerCredits.Add(new DLTGeneralLedgerAccount(memberGeneralLedgerToCashOut.JurisdictionMember!, GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(memberCreditCOAs!, GLAccountCode.BANK), amount ));

                // Jurisdiction
                // Create the jurisdiction Debit(s) portion of the journal entry
                jurisdictionDebits.Add(new DLTGeneralLedgerAccount(jurisdictionMemberGL.JurisdictionMember!,
                                                            GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionDebitCOAs!, GLAccountCode.BANK),
                                                            jurisdictionServiceMemberCashOutFee));
                // Create the jurisdiction Credit(s) portion of the journal entry
                jurisdictionCredits.Add(new DLTGeneralLedgerAccount(jurisdictionMemberGL.JurisdictionMember!,
                                                            GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionCreditCOAs!, GLAccountCode.EARNED_TRX_FEES),
                                                            jurisdictionServiceMemberCashOutFee));

                // Finally create the complete Journal Entry by adding all of the stakeholder's debit and credit portions
                je.AddDoubleEntry(sellerDebits, sellerCredits);
                je.AddDoubleEntry(jurisdictionDebits, jurisdictionCredits);
                string memo = $"Bank Cash-Out to destination Crypto Address: {destinationCryptoAccount.AddressID}";
                if (unitOfAmount == GeneralLedgerUnitOfAmountType.USD)
                {
                    memo += $" - (${originalAmount} @ {usdExchangeRate} per HBar)";
                }
                JournalEntryRecord journalEntryRecord = new JournalEntryRecord
                {
                    TransactionID = Guid.NewGuid().ToString(),
                    Memo = memo,
                    IsAutoReversal = false,
                    PostDate = timeManager.ProcessorUtcTime,
                    JournalEntry = je
                };
                #endregion

                #region Post Journal Entry to DLT
                trxConfirm = await _hederaDLTContext.JournalEntryTokenTransferAsync(journalEntryRecord,
                                                                                destinationCryptoAccount,
                                                                                transferOutToExternalAccount: true,  // Defunding a wallet requires transfering crypto TO an extrnal account, so we pass true here
                                                                                Convert.ToInt64(amount)
                                                                                ).ConfigureAwait(false);
                #endregion 
            }
            catch (DLTInsufficientBankFunds)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.Print($"Error in GeneralLedgerManager.CashOutMemberWalletAsync() - {ex}");
                throw;
            }
            return trxConfirm;
        }

        //private async Task<IDLTTransactionConfirmation> CashInOperatorWalletAsync(IDLTAddress sourceCryptoAccount,
        //                                                                        ulong? amount,
        //                                                                        object signatory = null!)
        //{
        //    return await CashInMemberWalletAsync(Guid.Empty.ToString(), sourceCryptoAccount, amount, signatory);
        //}

        private async Task<IDLTTransactionConfirmation?> CashOutOperatorWalletAsync(IDLTAddress sourcecryptoaccount,ulong? amount)
        {
            IDLTTransactionConfirmation? trxConfirm = null!;
            JournalEntryAccounts je = null!;
            try
            {
                #region Validate arguments
                if (amount == 0)
                {
                    throw new ArgumentException("Cash Out amount must be greater than zero.");
                }
                #endregion

                #region Determine the DLT General Ledger Chart of Accounts
                var jurisdictionMember = await GetMemberGeneralLedgerAsync(generalLedgerInstanceManifest?.JurisdictionID!).ConfigureAwait(false);
                var dAccountingServiceMember = await GetMemberGeneralLedgerAsync(generalLedgerInstanceManifest?.WCOGeneralLedgerServiceID!).ConfigureAwait(false);
                var jurisdictionGLDebitCOAs = _generalLedgerContext!.WorldComputerGeneralLedger!.DebitChartOfAccounts;
                var jurisdictionGLCreditCOAs = _generalLedgerContext!.WorldComputerGeneralLedger!.CreditChartOfAccounts;
                var dAccountingServiceGLDebitCOAs = generalLedgerInstanceManifest!.WCOGeneralLedgerServiceGeneralLedger!.DebitChartOfAccounts;
                var dAccountingServiceCreditCOAs = generalLedgerInstanceManifest!.WCOGeneralLedgerServiceGeneralLedger!.CreditChartOfAccounts;
                #endregion

                #region Determine Fees
                var dAccountingServiceFee = 0UL;
                var useFixedAccountingServiceFee = (generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.FeeRate <= 0);

                if (useFixedAccountingServiceFee)
                {
                    // If we are using a fixed fee - min and max must be the same
                    if (generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.MinimumFee != generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.MaximumFee)
                    {
                        throw new ArgumentException("For fixed jurisdiction cash out rate - min fee must equal max fee.");
                    }
                    dAccountingServiceFee = generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.MinimumFee;
                }
                else
                {
                    dAccountingServiceFee = Math.Min(Math.Max(generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.MinimumFee,
                                                    Convert.ToUInt64(amount * generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.FeeRate)),
                                                    generalLedgerInstanceManifest!.JurisdictionServiceCashOutFeeSchedule!.MaximumFee);
                }
                #endregion

                #region Verify Jurisdiction has enough funds in Wallet to cash out the requested amount
                var jurisdictionDebitWalletAccount = new DLTGeneralLedgerAccount(jurisdictionMember.JurisdictionMember!,
                                                                        GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionGLDebitCOAs!, GLAccountCode.BANK));
                var jurisdictionCreditWalletAccount = new DLTGeneralLedgerAccount(jurisdictionMember.JurisdictionMember!,
                                                                        GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionGLCreditCOAs!, GLAccountCode.BANK));
                var jurisdictionWalletAccountBalance = await GetBankAccountBalanceAsync(jurisdictionDebitWalletAccount, jurisdictionCreditWalletAccount).ConfigureAwait(false);  // Returns a negative number if balance is a Credit
                if (jurisdictionWalletAccountBalance < Convert.ToInt64(amount + dAccountingServiceFee))
                {
                    throw new DLTInsufficientBankFunds();  // %TODO% - For now...
                }
                #endregion

                #region Construct Journal Entry
                je = new JournalEntryAccounts(/*GLSParameters*/);
                // Create the Jurisdiction Debit(s) portion of the journal entry
                var jurisdictionDebits = new List<IDLTGeneralLedgerAccount>();
                jurisdictionDebits.Add(new DLTGeneralLedgerAccount(jurisdictionMember.JurisdictionMember!,
                                                            GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionGLDebitCOAs!, GLAccountCode.DUE_STAKEHOLDER),
                                                            amount));
                jurisdictionDebits.Add(new DLTGeneralLedgerAccount(jurisdictionMember.JurisdictionMember!,
                                                            GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionGLDebitCOAs!, GLAccountCode.WCO_TRX_FEES), dAccountingServiceFee));
                // Create the Jurisdiction Credit(s) portion of the journal entry
                var jurisdictionCredits = new List<IDLTGeneralLedgerAccount>();
                jurisdictionCredits.Add(new DLTGeneralLedgerAccount(jurisdictionMember.JurisdictionMember!,
                                                            GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(jurisdictionGLCreditCOAs!, GLAccountCode.BANK),
                                                            amount + dAccountingServiceFee));
                // Create the dAccounting Service Debit(s) portion of the journal entry
                var dAccountingServiceDebits = new List<IDLTGeneralLedgerAccount>();
                dAccountingServiceDebits.Add(new DLTGeneralLedgerAccount(dAccountingServiceMember.JurisdictionMember!,
                                                                GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(dAccountingServiceGLDebitCOAs!, GLAccountCode.BANK),
                                                                dAccountingServiceFee));
                // Create the dAccount Service Credit(s) portion of the journal entry
                var dAccountingServiceCredits = new List<IDLTGeneralLedgerAccount>();
                dAccountingServiceCredits.Add(new DLTGeneralLedgerAccount(dAccountingServiceMember.JurisdictionMember!,
                                                                GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(dAccountingServiceCreditCOAs!, GLAccountCode.EARNED_TRX_FEES),
                                                                dAccountingServiceFee));
                // Finally create the complete Journal Entry by adding all of the stakeholder's debit and credit portions
                je.AddDoubleEntry(jurisdictionDebits, jurisdictionCredits);
                je.AddDoubleEntry(dAccountingServiceDebits, dAccountingServiceCredits);
                JournalEntryRecord journalEntryRecord = new JournalEntryRecord
                {
                    TransactionID = Guid.NewGuid().ToString(),
                    Memo = "Operator Cash Out",
                    IsAutoReversal = false,
                    //PostDate = DateTime.UtcNow,
                    PostDate = timeManager.ProcessorUtcTime,
                    JournalEntry = je
                };
                #endregion
                //
                // *** %TODO% *** Need to also payout the dAccounting Service at the same time as the Jurisdiction is being paid out ***
                //
                #region Post Journal Entry to DLT
                trxConfirm = await _hederaDLTContext.JournalEntryTokenTransferAsync(journalEntryRecord,
                                                                                generalLedgerInstanceManifest!.JurisdictionPayorAddress!,  // The funds are sent to the Operator's Payer account
                                                                                transferOutToExternalAccount: true,  // Cashing out a wallet requires transfering crypto to extrnal account
                                                                                Convert.ToInt64(dAccountingServiceFee)
                                                                                ).ConfigureAwait(false);               
                #endregion 
            }
            catch (DLTInsufficientBankFunds)  // %TODO% -- For now
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.Print($"Error in GeneralLedgerManager.CashOutJurisdictionWalletAsync() - {ex}");
                throw;
            }
            return trxConfirm;
        }
        #endregion 


        #region Reports
        private async Task<IGeneralLedgerReports> GetMemberGeneralLedgerReportsAsync(IJurisdictionMemberGeneralLedger memberGL, GeneralLedgerReportOptions reportOptions,
                                                            DateTime? fromDateUtc = null, DateTime? toDateUtc = null)
        {
            IGeneralLedgerReports tb = null!;
            List<IDLTGeneralLedgerAccount> dbAccounts = new List<IDLTGeneralLedgerAccount>();
            List<IDLTGeneralLedgerAccount> crAccounts = new List<IDLTGeneralLedgerAccount>();
            try
            {
                #region Validate parameters
                if (memberGL == null || memberGL.GeneralLedger == null ||
                            memberGL.GeneralLedger?.DebitChartOfAccounts == null || memberGL.GeneralLedger?.CreditChartOfAccounts == null)
                {
                    throw new ArgumentException("Invalid General Ledger.");
                }
                #endregion 

                #region Get the DLT Balances for each postable GL account in parallel
                List<Task<ulong?>> balanceLookupTasks = new List<Task<ulong?>>();
                foreach (var acc in memberGL.GeneralLedger?.DebitChartOfAccounts!)  // Doesn't matter whether we use DebitChartOfAccounts or CreditChartOfAccounts as they have the same list of accounts
                {
                    DLTGeneralLedgerAccountInfo dbaccInfo = null!;
                    DLTGeneralLedgerAccountInfo craccInfo = null!;
                    // Only interested in postable accounts
                    if (glCatalog[acc.Code]!.Type == (int)GLAccountType.POSTABLE_GROUP_ACCOUNT)
                    {
                        // For each postable account we need to look up both its debit and its credit account balance
                        dbaccInfo = GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(memberGL.GeneralLedger?.DebitChartOfAccounts!, acc.Code);
                        craccInfo = GLAccountUtilities.LookupGLAccountInChartOfAccountsByCode(memberGL.GeneralLedger?.CreditChartOfAccounts!, acc.Code);
                        balanceLookupTasks.Add(_hederaDLTContext.GetTokenAccountBalanceAsync(generalLedgerInstanceManifest!.JurisdictionToken!, new DLTAddress(dbaccInfo.DLTAddress!))); // occupies even slots in the balanceLookupTasks list
                        balanceLookupTasks.Add(_hederaDLTContext.GetTokenAccountBalanceAsync(generalLedgerInstanceManifest!.JurisdictionToken!, new DLTAddress(craccInfo.DLTAddress!))); // occupies odd slots in the balanceLookupTasks list
                        dbAccounts.Add(new DLTGeneralLedgerAccount(memberGL.JurisdictionMember!, dbaccInfo));
                        crAccounts.Add(new DLTGeneralLedgerAccount(memberGL.JurisdictionMember!, craccInfo));
                    }
                }
                ulong?[] results = await Task.WhenAll<ulong?>(balanceLookupTasks).ConfigureAwait(false);

                // Process "even" results which are all debits
                int totalAccountCount = 0;
                ulong[] totals = new ulong[5];

                foreach (var dbacc in dbAccounts)
                {
                    dbacc.Amount = results[totalAccountCount];
                    totalAccountCount += 2;
                }
                // Process "odd" results which are all credits
                totalAccountCount = 1;
                foreach (var crbacc in crAccounts)
                {
                    crbacc.Amount = results[totalAccountCount];
                    totalAccountCount += 2;
                }
                #endregion

                #region Prepare final TrialBalance from results
                tb = new GeneralLedgerReports(memberGL!.GeneralLedger!.Description!, timeManager, fromDateUtc, toDateUtc, reportOptions,
                                        await GetCurrentHBarUSDExchangeRateAsync().ConfigureAwait(false),
                                        dbAccounts, crAccounts,
                                        generalLedgerInstanceManifest?.CultureInfo!,
                                        generalLedgerInstanceManifest?.JurisdictionDefaultAccountngCurrency!,
                                        glCatalog);
                #endregion 

            }
            catch (Exception ex)
            {
                Debug.Print($"Error in GeneralLedgerManager.GetTrailBalanceAsync() - {ex}");
            }
            return tb;
        }

        private string GetMemberChartOfAccountsAsync(IJurisdictionMemberGeneralLedger memberGL )
        {
            return LookupCoaTemplate(memberGL?.GeneralLedger?.COATemplateID!).
                            DumpChartOfAccounts(timeManager, memberGL!.JurisdictionMember!.GeneralLedger!.Description!, glCatalog);
        }
        #endregion

        #region General Ledger
        private async Task<List<IJournalEntryRecord>> ReadAllJournalEntryRecordsAsync(IJurisdictionMember worldComputerMember)
        {
            List<IJournalEntryRecord> allJournalEntries = new List<IJournalEntryRecord>();
            byte[] jeBlobIds = null!;
            // %TODO%  This code does not support very large files!!! - i.e. Length is an int
            lock (_journalEntriesFileStream)
            {
                int bytesRead = 0;
                int bytesToRead = Convert.ToInt32( _journalEntriesFileStream.Length);
                jeBlobIds = new byte[bytesToRead];
                _journalEntriesFileStream.Seek(0, SeekOrigin.Begin);
                while (bytesRead < bytesToRead)
                {
                  bytesRead += _journalEntriesFileStream.Read(jeBlobIds, bytesRead, bytesToRead - bytesRead);
                }
            }
            #region Read and Decrypt all Journal Entry blobs and add them to list
            byte[] currentJournalEntryBlobId = new byte[16];
            for(int i = 0; i < jeBlobIds.Length; i += 16 )
            {
                Buffer.BlockCopy(jeBlobIds, i, currentJournalEntryBlobId, 0, 16);
                if (! Guid.Empty.Equals( new Guid(currentJournalEntryBlobId)) )
                {
                    var journalEntryRecordSerializedBytes = await _generalLedgerContext.WorldComputerBlobStorage.ReadBlobAsync(
                                    new Guid(currentJournalEntryBlobId)).ConfigureAwait(false);
                    HostCryptology.DecryptBufferInPlaceWith32ByteKey(journalEntryRecordSerializedBytes, _generalLedgerContext.WorldComputerSymmetricKey);
                    allJournalEntries.Add(new JournalEntryRecord(journalEntryRecordSerializedBytes, GetJurisdictionMemberFromIDAsync, worldComputerMember));
                }
            }
            #endregion 
            return allJournalEntries;
        }


        private async Task<IJurisdictionMember> GetJurisdictionMemberFromIDAsync(string jurisdictionMemberId)
        {
            IJurisdictionMember member = null!;
            try
            {
                member = await _generalLedgerContext.GetJurisdictionMemberFromIDAsync(new Guid(jurisdictionMemberId)).ConfigureAwait(false);
                if (member == null || string.IsNullOrEmpty(member.ID))
                {
                    throw new FileNotFoundException();
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"Error in GeneralLedgerManager.GetMemberGeneralLedgerFromIDAsync() - {ex}");
                throw new FileNotFoundException();
            }
            return member;
        }


        private async Task SendGlobalGLJournalEntryEventAsync(Guid journalEntryBlobID)
        {
            #region Send Global GL_JOURNAL_ENTRY_POST event
            IGlobalEvent journalEntryGlobalEvent = null!;
            if (GlobalPropertiesContext.IsSimulatedNode())
            {
                journalEntryGlobalEvent = new GlobalEvent(_globalPropertiesContext!.SimulationPool![_globalPropertiesContext.SimulationNodeNumber],
                                                GlobalEventType.GL_JOURNAL_ENTRY_POST, journalEntryBlobID);
                #region Display Submission to Console
                if (!EntryPoint.AnimateNodesOnScreen)
                {
                    _globalPropertiesContext!.WriteToConsole!.DynamicInvoke($"Submitted Event: ", $"GL JOURNAL ENTRY POST", ConsoleColor.Yellow);
                }
                #endregion
            }
            else
            {
                journalEntryGlobalEvent = new GlobalEvent(_localNodeContext!.NodeDIDRef, GlobalEventType.GL_JOURNAL_ENTRY_POST, journalEntryBlobID);
            }
            await _globalEventSubscriptionManager.SubmitGlobalEventAsync(journalEntryGlobalEvent).ConfigureAwait(false);
            #endregion
        }



        private async Task<IGeneralLedger> GenerateJurisdictionMemberGeneralLedgerAsync(string? memberId, string? memberDescription, string? coaTemplateID)
        {
            GeneralLedger memberGL = null!;
            #region Validate arguments
            IChartOfAccountsTemplate coaTemplate = null!;
            if (string.IsNullOrEmpty(coaTemplateID))  // Empty or Null memberId indicates a regular jurisdiction member
            {
                coaTemplate = LookupCoaTemplate(generalLedgerInstanceManifest!.JurisdictionMemberCOAsTemplateID!);
            }
            else
            {
                coaTemplate = LookupCoaTemplate(coaTemplateID);
            }
             
            if (coaTemplate == null!)
            {
                throw new ArgumentException("Unknown chart of account template Id.");
            }
            #endregion

            try
            {
                #region IGNORE Get the Crypto account balance of the Jurisdiction Payor Account before we start
                //var cryptoPayorBefore = await _hederaDLTContext.GetCryptoAccountBalanceAsync(generalLedgerInstanceManifest!.JurisdictionPayorAddress!).ConfigureAwait(false);
                //if (cryptoPayorBefore.HasValue)
                //{
                //    if (cryptoPayorBefore.Value < 2_000_000)
                //    {
                //        throw new DLTInsufficientPayerFundsException();
                //    }
                //    Debug.Print($"Crypto Balance of Jurisdiction Payor Account {generalLedgerInstanceManifest!.JurisdictionPayorAddress!.AddressID} = {cryptoPayorBefore}.");
                //}
                //else
                //{
                //    Debug.Print($"Could not obtain Crypto Balance of Jurisdiction Payor Account {generalLedgerInstanceManifest!.JurisdictionPayorAddress!.AddressID}.");
                //}
                #endregion

                #region IGNORE: Check the Crypto balance on the Jurisidction Treasury account 
                //var cryptoBalance = await _hederaDLTContext.GetCryptoAccountBalanceAsync(generalLedgerInstanceManifest!.JurisdictionTokenTreasuryAddress!).ConfigureAwait(false);
                //if (cryptoBalance.HasValue)
                //{
                //    Debug.Print($"Crypto Balance of Jurisdiction Treasury Account {generalLedgerInstanceManifest!.JurisdictionTokenTreasuryAddress!.AddressID} = {cryptoBalance}.");
                //}
                //else
                //{
                //    Debug.Print($"Could not obtain Crypto Balance of Jurisdiction Treasury Account {generalLedgerInstanceManifest!.JurisdictionTokenTreasuryAddress!.AddressID}.");
                //}
                #endregion 

                #region IGNORE:  Check the Token Balance on the Jurisdiction Treasury account to ensure it is funded
                //var tokenBalance = await _hederaDLTContext.GetTokenAccountBalanceAsync(generalLedgerInstanceManifest!.JurisdictionToken!, generalLedgerInstanceManifest!.JurisdictionTokenTreasuryAddress!).ConfigureAwait(false);
                //if (tokenBalance.HasValue)
                //{
                //    Debug.Print($"Token {generalLedgerInstanceManifest!.JurisdictionToken!.Name} Balance of Juisdiction Treasury Account {generalLedgerInstanceManifest!.JurisdictionTokenTreasuryAddress!.AddressID} = {tokenBalance}.");
                //}
                //else
                //{
                //    Debug.Print($"Could not obtain Token Balance of Jurisdiction Treasury Account {generalLedgerInstanceManifest!.JurisdictionTokenTreasuryAddress!.AddressID}.");
                //}
                #endregion

                #region Generate required Jurisdiction GL Token Accounts 
                if (coaTemplate != null && coaTemplate.ChartOfAccounts != null)
                {
                    List<DLTGeneralLedgerAccountInfo>? debitChartOfAccounts = new List<DLTGeneralLedgerAccountInfo>();
                    List<DLTGeneralLedgerAccountInfo>? creditChartOfAccounts = new List<DLTGeneralLedgerAccountInfo>();
                    foreach (var glCode in coaTemplate.ChartOfAccounts)
                    {
                        // Lookup the properties for the glCode in the General Ledger Accounts Catalog
                        var glaccprop = glCatalog[glCode];
                        if (glaccprop != null)
                        {
                            // Only create DLT Account Addresess for "postable" GL accounts
                            if ((GLAccountType)glaccprop.Type == GLAccountType.POSTABLE_GROUP_ACCOUNT)
                            {
                                #region Generate a DLT Account Address to represent the postable debit GL account
                                //var dbAccount = await _hederaDLTContext.CreateTokenAccountAsync(
                                //                                                           generalLedgerInstanceManifest!.JurisdictionToken!,
                                //                                                           generalLedgerInstanceManifest!.JurisdictionTokenTreasuryAddress!
                                //                                                          ).ConfigureAwait(false);
                                var dbAccount = await _hederaDLTContext.CreatePlaceHolderTokenAccountAsync().ConfigureAwait(false);
                                if (dbAccount == null /*|| dbAccount.Status != "Success"*/)
                                {
                                    throw new InvalidOperationException($"Unable to create debit DLT account address for GL account {glCode}");
                                }
                                debitChartOfAccounts.Add(new DLTGeneralLedgerAccountInfo(glCode, dbAccount.AddressID!));
                                #endregion
                                #region Generate a DLT Account Address to represent the postable credit GL account
                                //var crAccount = await _hederaDLTContext.CreateTokenAccountAsync( 
                                //                                                           generalLedgerInstanceManifest!.JurisdictionToken!,
                                //                                                           generalLedgerInstanceManifest!.JurisdictionTokenTreasuryAddress!
                                //                                                          ).ConfigureAwait(false);
                                var crAccount = await _hederaDLTContext.CreatePlaceHolderTokenAccountAsync().ConfigureAwait(false);
                                if (crAccount == null /*|| dbAccount.Status != "Success"*/)
                                {
                                    throw new InvalidOperationException($"Unable to create debit DLT account address for GL account {glCode}");
                                }
                                creditChartOfAccounts.Add(new DLTGeneralLedgerAccountInfo(glCode, crAccount.AddressID!));
                                #endregion
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unknown GL Account {glCode}.");
                        }
                    }
                    #region Now create the Jurisdiction General Ledger
                    memberGL = new GeneralLedger
                    {
                        ID = memberId,
                        Description = memberDescription,
                        DebitChartOfAccounts = debitChartOfAccounts,
                        CreditChartOfAccounts = creditChartOfAccounts,
                        COATemplateID = coaTemplate.ID
                    };
                    #endregion
                }
                #endregion

                #region IGNORE Get the Crypto account balance of the Jurisdiction Payor Account after we are end 
                //var cryptoPayorAfter = await _hederaDLTContext.GetCryptoAccountBalanceAsync(generalLedgerInstanceManifest!.JurisdictionPayorAddress!).ConfigureAwait(false);
                //if (cryptoPayorAfter.HasValue)
                //{
                //    Debug.Print($"Crypto Balance of Jurisdiction Payor Account AFter {generalLedgerInstanceManifest!.JurisdictionPayorAddress!.AddressID} = {cryptoPayorAfter}.");
                //    Debug.Print($"Cost of Transaction in tinyHBar={cryptoPayorBefore - cryptoPayorAfter}");
                //}
                //else
                //{
                //    Debug.Print($"Could not obtain Crypto Balance of Jurisidiction Payor Account {generalLedgerInstanceManifest!.JurisdictionPayorAddress!.AddressID}.");
                //}
                #endregion
            }
            catch (Exception ex)
            {
                Debug.Print($"Error in GeneralLedgerManager.GenerateJurisdictionMemberGeneralLedgerAsync() - {ex}");
                throw;
            }
            return await Task.FromResult(memberGL);
        }
        #endregion 


        #region Miscellaneous
        private IChartOfAccountsTemplate LookupCoaTemplate(string coaTemplateId)
        {
            IChartOfAccountsTemplate coaTemplate = null!;
            foreach (var coatemp in coaTemplateList)
            {
                if (coatemp.ID!.Equals( coaTemplateId, StringComparison.OrdinalIgnoreCase))
                {
                    coaTemplate = coatemp;
                    break;
                }
            }
            return coaTemplate;
        }
        #endregion 
        #endregion
    }
}
