﻿using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Shared;

#nullable disable

namespace Certify.Models.Providers
{
    public interface IACMEClientProvider
    {
        string GetProviderName();

        string GetAcmeBaseURI();

        Task<bool> InitProvider(ILog log = null, AccountDetails account = null);

        Task<AcmeDirectoryInfo> GetAcmeDirectory();

        Task<string> GetAcmeAccountStatus();

        Task<ActionResult<AccountDetails>> AddNewAccountAndAcceptTOS(ILog log, string email, string eabKeyId = null, string eabKey = null, string eabKeyAlg = null, string importAccountURI = null, string importAccountKey = null);

        Task<bool> DeactivateAccount(ILog log);

        Task<ActionResult<AccountDetails>> UpdateAccount(ILog log, string email, bool termsAgreed);

        Task<PendingOrder> BeginCertificateOrder(ILog log, ManagedCertificate managedCertificate, bool resumeExistingOrder);

        Task<StatusMessage> SubmitChallenge(ILog log, string challengeType, PendingAuthorization pendingAuthorization);

        Task<PendingAuthorization> CheckValidationCompleted(ILog log, string challengeType, PendingAuthorization pendingAuthorization);

        Task<ProcessStepResult> CompleteCertificateRequest(ILog log, ManagedCertificate managedCertificate, string orderId, string pwd, string preferredChain, string defaultKeyType, bool useModernPFXBuildAlgs);

        Task<StatusMessage> RevokeCertificate(ILog log, ManagedCertificate managedCertificate);

        Task<ActionResult<AccountDetails>> ChangeAccountKey(ILog log, string newKeyPEM = null);

        Task<RenewalInfo> GetRenewalInfo(string certificateId);
    }
}
