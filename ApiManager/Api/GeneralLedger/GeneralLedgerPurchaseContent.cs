namespace UnoSysKernel
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using System.Text.Json;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;
    using UnoSys.Api.Models;
    using UnoSysCore;

    internal partial class ApiManager : SecuredKernelService, IApiManager 
    {
        public async Task<string> GeneralLedgerPurchaseContentAsync(string userSessionToken, string buyerSessionToken, string contentDIDRef, decimal price)
        {
            string result = "";
            #region Validate parameters
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("BuyerSessionToken", buyerSessionToken);
            ThrowIfParameterNullOrEmpty("ContentDIDRef", contentDIDRef);
            ThrowIfParameterNotPositivePrice("Price", price);
            #endregion 

            var ust = new UserSessionToken(userSessionToken);
            ISessionToken? typedSubjectSessionToken = typedSubjectSessionToken = SessionToken.GetTypeSessionToken(buyerSessionToken);


            //if (!wcContext.CheckResourceOwnerContext(ust, (SessionToken)typedSubjectSessionToken))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}
            //string report = "";
            if (typedSubjectSessionToken is UserSessionToken)
            {
                #region Lookup File locally on node using contentDIDRef passed in
                var contentFiles = Directory.GetFiles(LocalStorePathAbsoutePath, $"{contentDIDRef}*.cnt");
                if (contentFiles == null || contentFiles.Length == 0)
                {
                    throw new UnoSysResourceNotFoundException();
                }
                if (contentFiles.Length > 1)
                {
                    throw new UnoSysConflictException("Ambigous resource reference");
                }
                
                byte[] metadataBytes = await File.ReadAllBytesAsync(contentFiles[0]).ConfigureAwait(false);
                FILECLOSE_Operation metaData = JsonSerializer.Deserialize<FILECLOSE_Operation>(metadataBytes)!;
                var sellerMemberID = metaData.OwnerID;  // %TODO% we should be using this rather than seller's UserName
                #region Determine User Name of Seller from metadata of file
                // %TODO% - for now use hack to pull from content file name itself
                var indexContentFile = Path.GetFileName(contentFiles[0]);
                var pathWithoutCntExtension = Path.GetFileNameWithoutExtension(contentFiles[0]);
                var sellerUserName = Path.GetExtension(pathWithoutCntExtension).Substring(1);
                var purchaseDescription = Path.GetFileNameWithoutExtension(pathWithoutCntExtension).Substring(49);
                byte[] encryptedSellerMember = await wcContext.WorldComputerVirtualDriveBlobStorage!.ReadBlobAsync(
                                HostCryptology.ComputeUserHashFromUserName(sellerUserName)).ConfigureAwait(false);
                if (encryptedSellerMember == null)
                {
                    throw new UnoSysResourceNotFoundException();
                }
                #endregion
                #endregion

                #region Determine current USD to HBar exchange rate
                // %TODO%...
                decimal exchangeRate = 0.1m;  // for now assume 10 cents1

                #endregion

                #region Determine TBar Amount from Price and Exchange Rate
                var fundsAmountInTinyHBar = Convert.ToUInt64( price / exchangeRate * 100_000_000 );
                #endregion 

                var glBuyerMember = wcContext.GetJurisdictionMemberFromUserSessionToken((UserSessionToken)typedSubjectSessionToken);
                if (sellerMemberID != new Guid(glBuyerMember.ID!))
                {
                    var glSellerMember = wcContext.GetJurisdictionMemberFromEncryptedJurisdictionMemberBytes(encryptedSellerMember);
                    IDLTTransactionConfirmation? trxConfirm = await generalLedgerManager.PostSynchronousMemberPurchaseAsync(glBuyerMember, glSellerMember, fundsAmountInTinyHBar, purchaseDescription);
                    if (trxConfirm == null)
                    {
                        throw new InvalidOperationException("Failed to complte journal entry token transfer for unknown reason.");
                    }
                    else
                    {
                        if (trxConfirm.Status != "Success")
                        {
                            throw new InvalidOperationException($"Failed to complte journal entry token transfer - Status = {trxConfirm.Status}.");
                        }
                        else
                        {
                            result = JournalEntryAccounts.OutputJournalEntry(trxConfirm!.PostedJournalEntryRecord!.JournalEntry!.DebtAccountList!,
                                    trxConfirm.PostedJournalEntryRecord!.JournalEntry.CreditAccountList!,
                                    timeManager, generalLedgerManager.GeneralLedgerInstanceManifest.JurisdictionID!,
                                    generalLedgerManager.GeneralLedgerInstanceManifest.WCOGeneralLedgerServiceID!, generalLedgerManager.GeneralLedgerAccountsCatalog);
                            //result = trxConfirm.PostedJournalEntryRecord!.JournalEntry!.DumpJournalEntry(timeManager, generalLedgerManager.GeneralLedgerInstanceManifest.JurisdictionID!,
                            //                generalLedgerManager.GeneralLedgerInstanceManifest.WCOGeneralLedgerServiceID!, generalLedgerManager.GeneralLedgerAccountsCatalog);
                        }
                    }
                }
            }
            return result;
        }

       

        public string GeneralLedgerPurchaseContent(string userSessionToken, string buyerSessionToken, string contentDIDRef, decimal price)
        {
            return GeneralLedgerPurchaseContentAsync(userSessionToken, buyerSessionToken, contentDIDRef, price).Result;
        }
    }
}
