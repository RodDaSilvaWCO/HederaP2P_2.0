namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;
    using UnoSys.Api.Models;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task<string> GeneralLedgerGetReportAsync(string userSessionToken, string subjectSessionToken, int reportType, int reportOptions, string fromUtcDate, string toUtcDate)
        {
            #region Validate parameters
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            if( reportType <= (int)GeneralLedgerReportType.MIN_GENERAL_LEDGER_REPORT_TYPE || reportType >= (int)GeneralLedgerReportType.MAX_GENERAL_LEDGER_REPORT_TYPE )
            {
                throw new UnoSysArgumentException($"Parameter 'ReportType' is an invalid report type.");
            }
            //if (reportOptions <= (int)GeneralLedgerReportOptions.MIN_GENERAL_LEDGER_REPORT_OPTION_TYPE || reportOptions >= (int)GeneralLedgerReportOptions.MAX_GENERAL_LEDGER_REPORT_OPTION_TYPE)
            //{
            //    throw new UnoSysArgumentException($"Parameter 'ReportOptions' is an invalid report option.");
            //}

            DateTime utcToDate = timeManager.ProcessorUtcTime;                                                      // default to now (UTC)
            DateTime utcFromDate = new DateTime(utcToDate.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);       // default to Jan/1st of this year (UTC)
            if ( !string.IsNullOrEmpty(fromUtcDate) )
            {
                utcFromDate = ThrowIfParameterNotLegalDate("FromUtcDate",fromUtcDate);
            }
            if (!string.IsNullOrEmpty(toUtcDate))
            {
                utcToDate = ThrowIfParameterNotLegalDate("ToUtcDate", toUtcDate);
            }
            if (utcFromDate.Year != utcToDate.Year || utcFromDate.Year != DateTime.UtcNow.Year)
            {
                throw new UnoSysArgumentException($"Date parameters 'FromUtcDate' and 'ToUtcDate' must be in the current year.");
            }
            if (utcToDate < utcFromDate)
            {
                throw new UnoSysArgumentException($"Date parameter 'FromUtcDate' must be greater than or equal to ToUtcDate.");
            }
            #endregion 

            var ust = new UserSessionToken(userSessionToken);
            ISessionToken? typedSubjectSessionToken = null!;
            if (!string.IsNullOrEmpty(subjectSessionToken))
            {
                typedSubjectSessionToken = SessionToken.GetTypeSessionToken(subjectSessionToken);
            }
            else
            {
                // NOTE:  If subjectSessionToken is Null, assume WorldComputer OS User
                typedSubjectSessionToken = securityContext.WorldComputerOSUserSessionToken;
            }
            //if (!wcContext.CheckResourceOwnerContext(ust, (SessionToken)typedSubjectSessionToken))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}
            string report = "";
            if (typedSubjectSessionToken is UserSessionToken)
            {
                //var glMember = wcContext.ResolveJurisdictionMember((UserSessionToken) typedSubjectSessionToken);
                var glMember = wcContext.GetJurisdictionMemberFromUserSessionToken((UserSessionToken)typedSubjectSessionToken);
                report = await generalLedgerManager.GetReport(glMember, (GeneralLedgerReportType)reportType, 
                                    (GeneralLedgerReportOptions)reportOptions, utcFromDate, utcToDate ).ConfigureAwait(false);
            }
            return report;
        }

        public string GeneralLedgerGetReport(string userSessionToken, string subjectSessionToken, int reportType, int reportOutputType, string fromUtcDate, string toUtcDate)
        {
            return GeneralLedgerGetReportAsync(userSessionToken, subjectSessionToken, reportType, reportOutputType, fromUtcDate, toUtcDate).Result;
        }
    }
}
