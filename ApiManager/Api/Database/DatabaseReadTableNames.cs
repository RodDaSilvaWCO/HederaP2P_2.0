namespace UnoSysKernel
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string[]> DatabaseReadTableNamesAsync(string userSessionToken, string dbSessionToken)
        {
            return await Task.FromResult(wcContext.ReadDatabaseTableNames(new DatabaseSessionToken(dbSessionToken))).ConfigureAwait(false);
        }

        public string[] DatabaseReadTableNames(string userSessionToken, string dbSessionToken )
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("DatabaseSessionToken", dbSessionToken);

            var ust = new UserSessionToken(userSessionToken);
            var rst = new DatabaseSessionToken(dbSessionToken);
            if (!wcContext.CheckResourceOwnerContext(ust, rst))
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            wcContext.UnregisterDatabaseConnection(new DatabaseSessionToken(dbSessionToken));
            return DatabaseReadTableNamesAsync( userSessionToken, dbSessionToken).Result;
        }
    }
}
