namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;
    using UnoSys.Api.Models;
    using UnoSysCore;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string> GeneralLedgerTransferFundsAsync(string userSessionToken, string fromSessionToken, string toUserName, ulong fundsAmount, int unitOfAmount)
        {
            string result = "";
            #region Validate parameters
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("FromSessionToken", fromSessionToken);
            ThrowIfParameterNullOrEmpty("ToUserName", toUserName);
            ThrowIfParameterNotPositiveFundAmount("FundsAmount", fundsAmount);
            if (unitOfAmount <= (int)GeneralLedgerUnitOfAmountType.MIN_GENERAL_LEDGER_UNIT_OF_AMOUNT_TYPE || unitOfAmount >= (int)GeneralLedgerUnitOfAmountType.MAX_GENERAL_LEDGER_UNIT_OF_AMOUNT_TYPE)
            {
                throw new UnoSysArgumentException($"Parameter 'UnitOfAmount' is invalid.");
            }
            #endregion 

            var ust = new UserSessionToken(userSessionToken);
            ISessionToken? typedFromSessionToken = SessionToken.GetTypeSessionToken(fromSessionToken);
            if (typedFromSessionToken is UserSessionToken )
            {
                var glFromMember = wcContext.GetJurisdictionMemberFromUserSessionToken((UserSessionToken)typedFromSessionToken);
                var glToMember = await wcContext.GetJurisdictionMemberFromUserNameAsync(toUserName).ConfigureAwait(false);
                if( glFromMember.ID == glToMember.ID )
                {
                    throw new UnoSysConflictException("Cannot Transfer funds to self.");
                }
                IDLTTransactionConfirmation? trxConfirm = await generalLedgerManager.PostFundsTransferAsync( glFromMember, glToMember, fundsAmount, 
                                                                    (GeneralLedgerUnitOfAmountType) unitOfAmount).ConfigureAwait(false);
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

        public string GeneralLedgerTransferFunds(string userSessionToken, string fromSessionToken, string toUserName, ulong fundsAmount, int unitOfAmount)
        {
            return GeneralLedgerTransferFundsAsync(userSessionToken, fromSessionToken, toUserName, fundsAmount, unitOfAmount).Result;
        }
    }
}
