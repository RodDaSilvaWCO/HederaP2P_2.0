namespace UnoSysKernel
{
    using UnoSysCore;
    using System.Threading.Tasks;
    using System;
    using UnoSys.Api.Exceptions;
    using Microsoft.AspNetCore.Mvc.ApiExplorer;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string> UserCreateApplicationAsync(string userSessionToken, string subjectUserSessionToken, string applicaitonName, string applicationDescription, string appKey )
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("SubjectUserSessionToken", userSessionToken);
            ThrowIfParameterIsNotLegalIdentifier("ApplicationName", applicaitonName);
            ThrowIfParameterNullOrEmpty("ApplicationKey", appKey);
            Guid applicationKey = default(Guid);
            try
            {
                applicationKey = new Guid(appKey);
            }
            catch
            {
                throw new UnoSysArgumentException("Invalid ApplicationKey");
            }
            var ust = new UserSessionToken(userSessionToken);
            var sust = new UserSessionToken(subjectUserSessionToken);
            if (!wcContext.CheckResourceOwnerContext(ust, sust))
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            return await identityManager.UserCreateApplicationAsync(ust,  (ISessionToken)sust, applicaitonName, applicationDescription, applicationKey).ConfigureAwait(false);
        }

        public string  UserCreateApplication(string userSessionToken, string subjectUserSessionToken, string applicaitonName, string applicationDescription, string appKey)
        {
            return UserCreateApplicationAsync(userSessionToken, subjectUserSessionToken, applicaitonName, applicationDescription, appKey).Result;
        }
    }
}
