namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;
    using UnoSys.Api.Models;
    using UnoSysCore;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string> GeneralLedgerFundWalletAsync(string userSessionToken, string subjectSessionToken, string dltAddress, string dltPrivateKey, ulong fundsAmount, int unitOfAmount)
        {
            string result = "";
            #region Validate parameters
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("DLTAddress", dltAddress);
            ThrowIfParameterNullOrEmpty("DLTPrivateKey", dltPrivateKey);
            ThrowIfParameterNotPositiveFundAmount("FundsAmount", fundsAmount);
            if (unitOfAmount <= (int)GeneralLedgerUnitOfAmountType.MIN_GENERAL_LEDGER_UNIT_OF_AMOUNT_TYPE || unitOfAmount >= (int)GeneralLedgerUnitOfAmountType.MAX_GENERAL_LEDGER_UNIT_OF_AMOUNT_TYPE)
            {
                throw new UnoSysArgumentException($"Parameter 'UnitOfAmount' is invalid.");
            }
            #endregion 

            var ust = new UserSessionToken(userSessionToken);
            ISessionToken? typedSubjectSessionToken = null!;
            if (!string.IsNullOrEmpty(subjectSessionToken))
            {
                typedSubjectSessionToken = SessionToken.GetTypeSessionToken(subjectSessionToken);
            }
            else
            {
                // NOTE:  If subjectSessionToken is Null, assume WorldComputer OS User
                typedSubjectSessionToken = securityContext.WorldComputerOSUserSessionToken;
            }
            //if (!wcContext.CheckResourceOwnerContext(ust, (SessionToken)typedSubjectSessionToken))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}
            //string report = "";
            if (typedSubjectSessionToken is UserSessionToken)
            {
                //var glMember = wcContext.ResolveJurisdictionMember((UserSessionToken) typedSubjectSessionToken);
                var glMember = wcContext.GetJurisdictionMemberFromUserSessionToken((UserSessionToken)typedSubjectSessionToken);
                IDLTTransactionConfirmation? trxConfirm = await generalLedgerManager.FundMemberWalletAsync(glMember, dltAddress, dltPrivateKey, fundsAmount, 
                    (GeneralLedgerUnitOfAmountType)unitOfAmount).ConfigureAwait(false);
                if (trxConfirm != null)
                {
                    result = JournalEntryAccounts.OutputJournalEntry(trxConfirm!.PostedJournalEntryRecord!.JournalEntry!.DebtAccountList!,
                                    trxConfirm.PostedJournalEntryRecord!.JournalEntry.CreditAccountList!,
                                    timeManager, generalLedgerManager.GeneralLedgerInstanceManifest.JurisdictionID!,
                                    generalLedgerManager.GeneralLedgerInstanceManifest.WCOGeneralLedgerServiceID!, generalLedgerManager.GeneralLedgerAccountsCatalog);
                    //result = trxConfirm.PostedJournalEntryRecord!.JournalEntry!.DumpJournalEntry(timeManager, generalLedgerManager.GeneralLedgerInstanceManifest.JurisdictionID!,
                    //                generalLedgerManager.GeneralLedgerInstanceManifest.WCOGeneralLedgerServiceID!, generalLedgerManager.GeneralLedgerAccountsCatalog);
                }
            }
            return result;
        }

        public string GeneralLedgerFundWallet(string userSessionToken, string subjectSessionToken, string dltAdress, string dltPrivateKey, ulong fundsAmount, int unitOfAmount)
        {
            return GeneralLedgerFundWalletAsync(userSessionToken, subjectSessionToken, dltAdress, dltPrivateKey, fundsAmount, unitOfAmount).Result;
        }
    }
}
