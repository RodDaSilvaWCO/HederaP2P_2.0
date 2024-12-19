namespace UnoSysKernel
{
    using UnoSysCore;
    using System.Threading.Tasks;
    using System;
    using UnoSys.Api.Exceptions;
    //using static System.Net.Mime.MediaTypeNames;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task UserDeleteApplicationAsync(string userSessionToken, string subjectUserSessionToken, string applicationId)
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("SubjectUserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("ApplicationId", applicationId);
            Guid appId = default(Guid);
            try
            {
                appId = new Guid(applicationId);
            }
            catch
            {
                // We get here is applicationId is not a string representation of a Guid
                throw new UnoSysArgumentException("Invalid ApplicationId");
            }
            var ust = new UserSessionToken(userSessionToken);
            var sust = new UserSessionToken(subjectUserSessionToken);
            if (!wcContext.CheckResourceOwnerContext(ust, sust))
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            await identityManager.UserDeleteApplicationAsync(ust,  (ISessionToken)(sust), appId).ConfigureAwait(false);
        }

        public void UserDeleteApplication(string userSessionToken, string subjectUserSessionToken, string applicationId)
        {
            UserDeleteApplicationAsync(userSessionToken, subjectUserSessionToken, applicationId).Wait();
        }
    }
}
