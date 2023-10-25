﻿
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using Certify.Models;
using Certify.UI.ViewModel;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for Dashboard.xaml
    /// </summary>
    public partial class Dashboard : UserControl
    {
        public event FilterNotify FilterApplied; // filter notification event

        public class DailySummary : BindableBase
        {

        }

        public class SummaryModel : BindableBase
        {
            public int Total { get; set; }
            public int Healthy { get; set; }
            public int Error { get; set; }
            public int Warning { get; set; }
            public int AwaitingUser { get; set; }
            public int InvalidConfig { get; set; }

            public int NoCertificate { get; set; }

            public int TotalDomains { get; set; }

            public ObservableCollection<DailySummary> DailyRenewals { get; set; }
        }
        public SummaryModel ViewModel { get; set; } = new SummaryModel();

        protected ViewModel.AppViewModel _appViewModel => AppViewModel.Current;

        public Dashboard()
        {
            InitializeComponent();

            DataContext = ViewModel;

            _appViewModel.PropertyChanged -= AppViewModel_PropertyChanged;
            _appViewModel.PropertyChanged += AppViewModel_PropertyChanged;
        }

        private void AppViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ManagedCertificates")
            {
                RefreshSummary();
            }
        }

        public void RefreshSummary()
        {

            if (AppViewModel.Current.ManagedCertificates?.Any() == true)
            {

                var ms = AppViewModel.Current.ManagedCertificates;

                ViewModel.Total = ms.Count();
                ViewModel.Healthy = ms.Count(c => c.Health == ManagedCertificateHealth.OK);
                ViewModel.Error = ms.Count(c => c.Health == ManagedCertificateHealth.Error);
                ViewModel.Warning = ms.Count(c => c.Health == ManagedCertificateHealth.Warning);
                ViewModel.AwaitingUser = ms.Count(c => c.Health == ManagedCertificateHealth.AwaitingUser);
                ViewModel.NoCertificate = ms.Count(c => c.CertificatePath == null);

                // count items with invalid config (e.g. multiple primary domains)
                ViewModel.InvalidConfig = ms.Count(c => c.DomainOptions.Count(d => d.IsPrimaryDomain) > 1);

                ViewModel.TotalDomains = ms.Sum(s => s.RequestConfig.SubjectAlternativeNames.Count());

                PanelTotal.Visibility = ViewModel.Total == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                PanelHealthy.Visibility = ViewModel.Healthy == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                PanelError.Visibility = ViewModel.Error == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                PanelWarning.Visibility = ViewModel.Warning == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                PanelAwaitingUser.Visibility = ViewModel.AwaitingUser == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

                Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                ViewModel.Total = 0;
                ViewModel.Healthy = 0;
                ViewModel.Error = 0;
                ViewModel.Warning = 0;
                ViewModel.AwaitingUser = 0;
                ViewModel.InvalidConfig = 0;
                ViewModel.NoCertificate = 0;

                ViewModel.TotalDomains = 0;

                Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void Hyperlink_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender != null)
            {
                var filter = (sender as System.Windows.Documents.Hyperlink).Tag.ToString();
                FilterApplied.Invoke(filter);
            }
        }
    }
}
