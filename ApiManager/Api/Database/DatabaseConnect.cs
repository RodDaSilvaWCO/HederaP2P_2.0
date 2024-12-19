namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api.Models;
    using UnoSys.Api.Exceptions;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string> DatabaseConnectAsync(string userSessionToken, string sessionToken, string databaseName, DatabaseAccessType desiredAccess, DatabaseShareType shareMode)
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("SessionToken", sessionToken);
            ThrowIfParameterIsNotLegalIdentifier("DatabaseName", databaseName);
            if (desiredAccess == DatabaseAccessType.Unitialized)
            {
                throw new UnoSysArgumentException("Parameter 'DesiredAccess' is uninitialized.");
            }
            if (shareMode == DatabaseShareType.Unititialized)
            {
                throw new UnoSysArgumentException("Parameter 'ShareMode' is uninitialized.");
            }

            //SessionType sessionTokenType = SessionToken.GetSessionTokenType(sessionToken);
            var ust = new UserSessionToken(userSessionToken);
            ISessionToken? resourceSessionToken = SessionToken.GetTypeSessionToken(sessionToken);


            //SessionToken? resourceSessionToken = null!;
            //if (sessionTokenType == SessionType.Application)
            //{
            //    resourceSessionToken = new ApplicationSessionToken(sessionToken);

            //}
            //else if(sessionTokenType == SessionType.User)
            //{
            //    resourceSessionToken = new UserSessionToken(sessionToken);
            //}
            //else
            //{
            //    throw new UnoSysResourceNotFoundException();
            //}

            if (!wcContext.CheckResourceOwnerContext(ust, (SessionToken)resourceSessionToken))
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            return (await securityContext.DatabaseConnectAsync((SessionToken)resourceSessionToken, databaseName, desiredAccess, shareMode).ConfigureAwait(false)).TokenRef;
        }

        public string DatabaseConnect(string userSessionToken, string sessionToken, string databaseName, DatabaseAccessType desiredAccess, DatabaseShareType shareMode)
        {
            return DatabaseConnectAsync(userSessionToken, sessionToken, databaseName, desiredAccess, shareMode).Result;
        }
    }
}