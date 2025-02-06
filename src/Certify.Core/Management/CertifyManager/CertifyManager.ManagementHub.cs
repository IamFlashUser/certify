using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Locales;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Shared.Core.Utils;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private IManagementServerClient _managementServerClient;
        private string _managementServerConnectionId = string.Empty;

        public async Task<ActionStep> UpdateManagementHub(string url, string joiningKey)
        {

            _serverConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();
            _serverConfig.ManagementServerHubUri = url;
            SharedUtils.ServiceConfigManager.StoreUpdatedAppServiceConfig(_serverConfig);

            _managementServerClient = null;

            try
            {
                await EnsureMgmtHubConnection();
            }
            catch
            {
                return new ActionStep("Update Management Hub Failed", "A problem occurred when connecting to the management hub. Check URL.", hasError: true);
            }

            return new ActionStep("Updated Management Hub", "OK", false);
        }

        public void SetDirectManagementClient(IManagementServerClient client)
        {
            _managementServerClient = client;
        }

        private async Task EnsureMgmtHubConnection()
        {
            // connect/reconnect to management hub if enabled
            if (_managementServerClient == null || !_managementServerClient.IsConnected())
            {
                var mgmtHubUri = Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB") ?? _serverConfig.ManagementServerHubUri;

                if (!string.IsNullOrWhiteSpace(mgmtHubUri))
                {
                    await StartManagementHubConnection(mgmtHubUri);
                }
            }
            else
            {

                // send heartbeat message to management hub
                SendHeartbeatToManagementHub();
            }
        }

        private void SendHeartbeatToManagementHub()
        {
            _managementServerClient.SendInstanceInfo(Guid.NewGuid(), false);
        }

        public ManagedInstanceInfo GetManagedInstanceInfo()
        {
            return new ManagedInstanceInfo
            {
                InstanceId = InstanceId,
                Title = $"{Environment.MachineName}",
                OS = EnvironmentUtil.GetFriendlyOSName(detailed: false),
                OSVersion = EnvironmentUtil.GetFriendlyOSName(),
                ClientVersion = Util.GetAppVersion().ToString(),
                ClientName = ConfigResources.AppName
            };
        }
        private async Task StartManagementHubConnection(string hubUri)
        {

            _serviceLog.Debug("Attempting connection to management hub {hubUri}", hubUri);

            var appVersion = Util.GetAppVersion().ToString();

            var instanceInfo = GetManagedInstanceInfo();

            if (_managementServerClient != null)
            {
                _managementServerClient.OnGetCommandResult -= PerformHubCommandWithResult;
                _managementServerClient.OnConnectionReconnecting -= _managementServerClient_OnConnectionReconnecting;
            }

            _managementServerClient = new ManagementServerClient(hubUri, instanceInfo);

            try
            {
                await _managementServerClient.ConnectAsync();

                _managementServerClient.OnGetCommandResult += PerformHubCommandWithResult;
                _managementServerClient.OnConnectionReconnecting += _managementServerClient_OnConnectionReconnecting;
            }
            catch (Exception ex)
            {
                _serviceLog.Error(ex, "Failed to create connection to management hub {hubUri}", hubUri);

                _managementServerClient = null;
            }
        }

        public async Task<InstanceCommandResult> PerformHubCommandWithResult(InstanceCommandRequest arg)
        {
            object val = null;

            if (arg.CommandType == ManagementHubCommands.GetManagedItem)
            {
                // Get a single managed item by id
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");
                val = await GetManagedCertificate(managedCertIdArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.GetManagedItems)
            {
                // Get all managed items
                var items = await GetManagedCertificates(new ManagedCertificateFilter { });
                val = new ManagedInstanceItems { InstanceId = InstanceId, Items = items };
            }
            else if (arg.CommandType == ManagementHubCommands.GetStatusSummary)
            {
                var s = await GetManagedCertificateSummary(new ManagedCertificateFilter { });
                s.InstanceId = InstanceId;
                val = s;
            }
            else if (arg.CommandType == ManagementHubCommands.GetManagedItemLog)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");
                var limit = args.FirstOrDefault(a => a.Key == "limit");

                val = await GetItemLog(managedCertIdArg.Value, int.Parse(limit.Value));
            }
            else if (arg.CommandType == ManagementHubCommands.GetManagedItemRenewalPreview)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var managedCertArg = args.FirstOrDefault(a => a.Key == "managedCert");
                var managedCert = JsonSerializer.Deserialize<ManagedCertificate>(managedCertArg.Value);

                val = await GeneratePreview(managedCert);
            }
            else if (arg.CommandType == ManagementHubCommands.ExportCertificate)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");
                var format = args.FirstOrDefault(a => a.Key == "format");
                val = await ExportCertificate(managedCertIdArg.Value, format.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.UpdateManagedItem)
            {
                // update a single managed item 
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var managedCertArg = args.FirstOrDefault(a => a.Key == "managedCert");
                var managedCert = JsonSerializer.Deserialize<ManagedCertificate>(managedCertArg.Value);

                var item = await UpdateManagedCertificate(managedCert);

                val = item;

                ReportManagedItemUpdateToMgmtHub(item);
            }
            else if (arg.CommandType == ManagementHubCommands.RemoveManagedItem)
            {
                // delete a single managed item 
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");

                var actionResult = await DeleteManagedCertificate(managedCertIdArg.Value);

                val = actionResult;

                if (actionResult.IsSuccess)
                {
                    ReportManagedItemDeleteToMgmtHub(managedCertIdArg.Value);
                }
            }
            else if (arg.CommandType == ManagementHubCommands.TestManagedItemConfiguration)
            {
                // test challenge response config for a single managed item 
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var managedCertArg = args.FirstOrDefault(a => a.Key == "managedCert");
                var managedCert = JsonSerializer.Deserialize<ManagedCertificate>(managedCertArg.Value);

                var log = ManagedCertificateLog.GetLogger(managedCert.Id, _loggingLevelSwitch);

                val = await TestChallenge(log, managedCert, isPreviewMode: true);

            }
            else if (arg.CommandType == ManagementHubCommands.PerformManagedItemRequest)
            {
                // attempt certificate order
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var managedCertIdArg = args.FirstOrDefault(a => a.Key == "managedCertId");
                var managedCert = await GetManagedCertificate(managedCertIdArg.Value);

                var progressState = new RequestProgressState(RequestState.Running, "Starting..", managedCert);
                var progressIndicator = new Progress<RequestProgressState>(progressState.ProgressReport);

                _ = await PerformCertificateRequest(
                                                        null,
                                                        managedCert,
                                                        progressIndicator,
                                                        resumePaused: true,
                                                        isInteractive: true
                                                        );

                val = true;
            }
            else if (arg.CommandType == ManagementHubCommands.GetCertificateAuthorities)
            {
                val = await GetCertificateAuthorities();
            }
            else if (arg.CommandType == ManagementHubCommands.UpdateCertificateAuthority)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var itemArg = args.FirstOrDefault(a => a.Key == "certificateAuthority");
                var item = JsonSerializer.Deserialize<CertificateAuthority>(itemArg.Value);

                val = await UpdateCertificateAuthority(item);
            }
            else if (arg.CommandType == ManagementHubCommands.RemoveCertificateAuthority)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var itemArg = args.FirstOrDefault(a => a.Key == "id");
                val = await RemoveCertificateAuthority(itemArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.GetAcmeAccounts)
            {
                val = await GetAccountRegistrations();
            }
            else if (arg.CommandType == ManagementHubCommands.AddAcmeAccount)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var registrationArg = args.FirstOrDefault(a => a.Key == "registration");
                var registration = JsonSerializer.Deserialize<ContactRegistration>(registrationArg.Value);

                val = await AddAccount(registration);
            }
            else if (arg.CommandType == ManagementHubCommands.RemoveAcmeAccount)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var itemArg = args.FirstOrDefault(a => a.Key == "storageKey");
                var deactivateArg = args.FirstOrDefault(a => a.Key == "deactivate");
                val = await RemoveAccount(itemArg.Value, bool.Parse(deactivateArg.Value));
            }
            else if (arg.CommandType == ManagementHubCommands.GetStoredCredentials)
            {
                val = await _credentialsManager.GetCredentials();
            }
            else if (arg.CommandType == ManagementHubCommands.UpdateStoredCredential)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var itemArg = args.FirstOrDefault(a => a.Key == "item");
                var storedCredential = JsonSerializer.Deserialize<StoredCredential>(itemArg.Value);

                val = await _credentialsManager.Update(storedCredential);
            }
            else if (arg.CommandType == ManagementHubCommands.RemoveStoredCredential)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var itemArg = args.FirstOrDefault(a => a.Key == "storageKey");
                val = await _credentialsManager.Delete(_itemManager, itemArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.GetChallengeProviders)
            {
                val = await Core.Management.Challenges.ChallengeProviders.GetChallengeAPIProviders();
            }

            else if (arg.CommandType == ManagementHubCommands.GetDnsZones)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var providerTypeArg = args.FirstOrDefault(a => a.Key == "providerTypeId");
                var credentialIdArg = args.FirstOrDefault(a => a.Key == "credentialId");

                val = await GetDnsProviderZones(providerTypeArg.Value, credentialIdArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.GetDeploymentProviders)
            {
                val = await GetDeploymentProviders();
            }
            else if (arg.CommandType == ManagementHubCommands.ExecuteDeploymentTask)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);

                var managedCertificateIdArg = args.FirstOrDefault(a => a.Key == "managedCertificateId");
                var taskIdArg = args.FirstOrDefault(a => a.Key == "taskId");

                val = await PerformDeploymentTask(null, managedCertificateIdArg.Value, taskIdArg.Value, isPreviewOnly: false, skipDeferredTasks: false, forceTaskExecution: false);
            }
            else if (arg.CommandType == ManagementHubCommands.GetTargetServiceTypes)
            {
                val = await GetTargetServiceTypes();
            }
            else if (arg.CommandType == ManagementHubCommands.GetTargetServiceItems)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var serviceTypeArg = args.FirstOrDefault(a => a.Key == "serviceType");

                var serverType = MapStandardServerType(serviceTypeArg.Value);

                val = await GetPrimaryWebSites(serverType, ignoreStoppedSites: true);
            }
            else if (arg.CommandType == ManagementHubCommands.GetTargetServiceItemIdentifiers)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(arg.Value);
                var serviceTypeArg = args.FirstOrDefault(a => a.Key == "serviceType");
                var itemArg = args.FirstOrDefault(a => a.Key == "itemId");

                var serverType = MapStandardServerType(serviceTypeArg.Value);

                val = await GetDomainOptionsFromSite(serverType, itemArg.Value);
            }
            else if (arg.CommandType == ManagementHubCommands.Reconnect)
            {
                await _managementServerClient.Disconnect();
            }

            return new InstanceCommandResult { CommandId = arg.CommandId, Value = JsonSerializer.Serialize(val), ObjectValue = val };
        }

        private StandardServerTypes MapStandardServerType(string type)
        {
            if (StandardServerTypes.TryParse(type, out StandardServerTypes standardServerType))
            {
                return standardServerType;
            }
            else
            {
                return StandardServerTypes.Other;
            }
        }

        private void ReportManagedItemUpdateToMgmtHub(ManagedCertificate item)
        {
            if (item != null)
            {
                _managementServerClient?.SendNotificationToManagementHub(ManagementHubCommands.NotificationUpdatedManagedItem, item);
            }
        }
        private void ReportManagedItemDeleteToMgmtHub(string id)
        {
            _managementServerClient?.SendNotificationToManagementHub(ManagementHubCommands.NotificationRemovedManagedItem, id);
        }

        private void ReportRequestProgressToMgmtHub(RequestProgressState progress)
        {
            _managementServerClient?.SendNotificationToManagementHub(ManagementHubCommands.NotificationManagedItemRequestProgress, progress);
        }

        private void _managementServerClient_OnConnectionReconnecting()
        {
            _serviceLog.Warning("Reconnecting to Management Hub.");
        }

        private void GenerateDemoItems()
        {
            var items = DemoDataGenerator.GenerateDemoItems();
            foreach (var item in items)
            {
                _ = UpdateManagedCertificate(item);
            }
        }
    }
}
