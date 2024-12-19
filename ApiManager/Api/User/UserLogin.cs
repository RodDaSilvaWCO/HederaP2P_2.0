namespace UnoSysKernel
{
    //using Oqtane.Shared;
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string> UserLoginAsync(/*string userSessionToken,*/ string userName, string password )
        {
            // NOTE:  **** This API is only intended to be called internally within the WorldComputer
            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("UserName", userName);
            ThrowIfParameterNullOrEmpty("Password", password);
            return (await identityManager.UserLoginAsync(/*new UserSessionToken(userSessionToken),*/ userName, password).ConfigureAwait(false)).TokenRef;
        }

        public string UserLogin(/*string userSessionToken,*/ string userName, string password)
        {
            return UserLoginAsync(/*userSessionToken,*/ userName, password).Result;
        }
    }
}
