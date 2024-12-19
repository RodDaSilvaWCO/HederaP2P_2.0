namespace UnoSysKernel
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string[]> ApplicationReadDatabaseNamesAsync(string userSessionToken, string appSessionToken)
        {
            return await Task.FromResult(ApplicationReadDatabaseNames(userSessionToken, appSessionToken));
        }

        public string[] ApplicationReadDatabaseNames(string userSessionToken, string appSessionToken)
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("SessionToken", appSessionToken);

            var ust = new UserSessionToken(userSessionToken);
            var ast = new ApplicationSessionToken(appSessionToken);
            #region Security Check to ensure appSessionToken was created in the conect of the passed in userSessionToken
            if (!wcContext.CheckResourceOwnerContext(ust, ast))
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            #endregion 
            return securityContext.ResolveDatabaseNames( ast);
        }
    }
}
