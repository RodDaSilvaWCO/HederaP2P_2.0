namespace UnoSysKernel
{
    using UnoSysCore;
    using System.Threading.Tasks;
    using System;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task ApplicationCreateDatabaseAsync(string userSessionToken, string appSessionToken, string databaseName, string databaseDescription )
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("ApplicationSessionToken", appSessionToken);
            ThrowIfParameterIsNotLegalIdentifier("DatabaseName", databaseName);

            var ust = new UserSessionToken(userSessionToken);
            var ast = new ApplicationSessionToken(appSessionToken);
            #region Security Check to ensure appSessionToken was created in the context of the passed in userSessionToken
            if (!wcContext.CheckResourceOwnerContext(ust, ast))
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            #endregion 
            await securityContext.CreateDatabaseAsync(ust,  (ISessionToken)(ast),  databaseName,  databaseDescription).ConfigureAwait(false);
        }

        public void ApplicationCreateDatabase(string userSessionToken, string appSessionToken, string databaseName, string databaseDescription)
        {
            ApplicationCreateDatabaseAsync(userSessionToken,  appSessionToken,  databaseName,  databaseDescription).Wait();
        }
    }
}
