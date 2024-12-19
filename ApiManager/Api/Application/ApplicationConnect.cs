namespace UnoSysKernel
{
    using UnoSysCore;
    using System.Threading.Tasks;
    using System;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string> ApplicationConnectAsync(string userSessionToken, string encryptdAppDidRef, string appKey )
        {
            #region Validate Parameters
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("ApplicationId", encryptdAppDidRef);
            ThrowIfParameterNullOrEmpty("AppKey", appKey);
            #endregion 
            // %TODO% - Check permissions on UserSessionToken
            return (await securityContext.ApplicationConnectAsync(new UserSessionToken(userSessionToken), encryptdAppDidRef, appKey).ConfigureAwait(false)).TokenRef;
        }

        public string ApplicationConnect(string userSessionToken, string encryptdAppDidRef, string appKey)
        {
            return ApplicationConnectAsync(userSessionToken, encryptdAppDidRef, appKey).Result;
        }

        private void ValidateParameters(string userSessionToken, string encryptdAppDidRef, string appKey)
        {

        }
    }
}
