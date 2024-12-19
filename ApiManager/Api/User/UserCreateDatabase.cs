namespace UnoSysKernel
{
    using UnoSysCore;
    using System.Threading.Tasks;
    using System;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task UserCreateDatabaseAsync(string userSessionToken, string subjectUserSessionToken, string databaseName, string databaseDescription )
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
            await securityContext.CreateDatabaseAsync(ust,  (ISessionToken)sust,  databaseName,  databaseDescription).ConfigureAwait(false);
        }

        public void UserCreateDatabase(string userSessionToken, string subjectUserSessionToken, string databaseName, string databaseDescription)
        {
            UserCreateDatabaseAsync(userSessionToken, subjectUserSessionToken,  databaseName,  databaseDescription).Wait();
        }
    }
}
