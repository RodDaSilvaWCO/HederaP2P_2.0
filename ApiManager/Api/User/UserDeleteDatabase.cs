namespace UnoSysKernel
{
    using UnoSysCore;
    using System.Threading.Tasks;
    using System;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task UserDeleteDatabaseAsync(string userSessionToken, string subjectUserSessionToken, string databaseName)
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("SubjectUserSessionToken", subjectUserSessionToken);
            ThrowIfParameterIsNotLegalIdentifier("DatabaseName", databaseName);

            var ust = new UserSessionToken(userSessionToken);
            var sust = new UserSessionToken(subjectUserSessionToken);
            if (!wcContext.CheckResourceOwnerContext(ust, sust))
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            try
            {
                await securityContext.DeleteDatabaseAsync(ust, (ISessionToken)(sust), databaseName).ConfigureAwait(false);
            }
            catch (Exception)
            {
                throw;
            }
            
        }

        public void UserDeleteDatabase(string userSessionToken, string subjectUserSessionToken, string databaseName)
        {
            UserDeleteDatabaseAsync(userSessionToken, subjectUserSessionToken,  databaseName).Wait();
        }
    }
}
