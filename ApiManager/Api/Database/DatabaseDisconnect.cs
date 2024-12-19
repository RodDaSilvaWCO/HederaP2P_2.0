namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task DatabaseDisconnectAsync(string userSessionToken, string dbSessionToken)
        {
            DatabaseDisconnect(userSessionToken, dbSessionToken);
            await Task.CompletedTask;
        }


        public void DatabaseDisconnect(string userSessionToken, string dbSessionToken)
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
        }

    }
}
