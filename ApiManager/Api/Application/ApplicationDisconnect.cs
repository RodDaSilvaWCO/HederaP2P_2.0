namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task ApplicationDisconnectAsync( string userSessionToken, string appSessionToken )
        {
            ApplicationDisconnect(userSessionToken, appSessionToken);
            await Task.CompletedTask;
        }

        public void ApplicationDisconnect(string userSessionToken, string appSessionToken)
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("SessionToken", appSessionToken);

            var ust = new UserSessionToken(userSessionToken);
            var ast = new ApplicationSessionToken(appSessionToken);
            #region Security Check to ensure appSessionToken was created in the current userSessionToken
            if (!wcContext.CheckResourceOwnerContext(ust, ast))
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            #endregion 
            wcContext!.UnregisterApplicationConnection(ast);
        }
    }
}
