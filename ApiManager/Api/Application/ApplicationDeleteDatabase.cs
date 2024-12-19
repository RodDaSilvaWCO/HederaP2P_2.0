namespace UnoSysKernel
{
    using UnoSysCore;
    using System.Threading.Tasks;
    using System;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task ApplicationDeleteDatabaseAsync(string userSessionToken, string appSessionToken, string databaseName)
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("ApplicationSessionToken", appSessionToken);
            ThrowIfParameterIsNotLegalIdentifier("DatabaseName", databaseName);

            var ust = new UserSessionToken(userSessionToken);
            var rst = new ApplicationSessionToken(appSessionToken);
            if (!wcContext.CheckResourceOwnerContext(ust, rst))
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            await securityContext.DeleteDatabaseAsync(ust,  (ISessionToken)(rst),  databaseName).ConfigureAwait(false);
        }

        public void ApplicationDeleteDatabase(string userSessionToken, string appSessionToken, string databaseName)
        {
            ApplicationDeleteDatabaseAsync(userSessionToken,  appSessionToken,  databaseName).Wait();
        }
    }
}
