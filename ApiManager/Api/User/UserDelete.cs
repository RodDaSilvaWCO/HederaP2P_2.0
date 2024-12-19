namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task UserDeleteAsync(string userSessionToken, string userName, string password )
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("UserName", userName);
            ThrowIfParameterNullOrEmpty("Password", password);
            //Guid UserID = default(Guid);
            //try
            //{
            //    UserID = new Guid(userId);
            //}
            //catch
            //{
            //    // We get here is userId is not a string representation of a Guid
            //    throw new UnoSysArgumentException("Invalid UserId");
            //}
            await identityManager.UserDeleteAsync(new UserSessionToken(userSessionToken), userName, password ).ConfigureAwait(false);
        }

        public void UserDelete(string userSessionToken, string userName, string password)
        {
            UserDeleteAsync(userSessionToken, userName, password).Wait();
        }
    }
}
