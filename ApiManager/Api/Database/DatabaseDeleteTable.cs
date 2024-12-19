namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;
    using UnoSys.Api.Interfaces;
    using UnoSys.Api.Models;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task DatabaseDeleteTableAsync(string userSessionToken, string databaseSessionToken, string tableName)
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("DatabaseSessionToken", databaseSessionToken);
            ThrowIfParameterIsNotLegalIdentifier("TableName", tableName);

            var ust = new UserSessionToken(userSessionToken);
            var rst = new DatabaseSessionToken(databaseSessionToken);
            if (!wcContext.CheckResourceOwnerContext(ust, rst))
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            await securityContext.DatabaseDeleteTableAsync(ust, rst, tableName).ConfigureAwait(false);
        }

        public void DatabaseDeleteTable(string userSessionToken, string databaseSessionToken, string tableName)
        {
            DatabaseDeleteTableAsync( userSessionToken, databaseSessionToken, tableName).Wait();
        }
    }
}