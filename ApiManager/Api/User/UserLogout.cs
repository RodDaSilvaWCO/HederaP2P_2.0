namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task UserLogoutAsync(/*string userSessionToken,*/ string subjectUserSessionToken)
        {
            // NOTE:  **** This API is only intended to be called internally within the WorldComputer
            //ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("SubjectUserSessionToken", subjectUserSessionToken);
            // %TODO% - Ensure subjectUserSessionToken is the same as userSessionToken??
            //var ust = new UserSessionToken(userSessionToken);
            //var sust = new UserSessionToken(subjectUserSessionToken);
            //if (!wcContext.CheckResourceOwnerContext(ust, sust))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}
            try
            {
                await identityManager.UserLogoutAsync(new UserSessionToken(subjectUserSessionToken)).ConfigureAwait(false);
            }
            catch (UnoSysResourceNotFoundException)
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            catch(Exception)
            {
                throw;
            }
        }

        public void UserLogout(/*string userSessionToken,*/ string subjectUserSessionToken)
        {
            UserLogoutAsync(/*userSessionToken,*/ subjectUserSessionToken).Wait();
        }
    }
}
