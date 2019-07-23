using Google.Apis.MyBusiness.v4;
using Google.Apis.MyBusiness.v4.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;
using System.Windows.Data;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Documents;
using Google.Apis.Auth.OAuth2;

namespace GetReviews
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public partial class MainWindow
    {
        // The scope of Google My Business API
        const string MybusinessServiceScope = "https://www.googleapis.com/auth/business.manage";

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            LbAccounts.Items.Clear();
            LbStores.Items.Clear();
            TxtLog.Clear();
            TxtUpdates.Clear();

            (ConcurrentBag<DlQuestion> allQuestions, ConcurrentBag<DlReview> allReviews) = await ReloadAsync(Name,tbAccount.Text).ConfigureAwait(false);

            await Dispatcher.InvokeAsync(() =>
            {
                DlQuestions questions = (DlQuestions) Resources["Questions"];
                questions.Clear();
                foreach (DlQuestion x in allQuestions)
                {
                    questions.Add(x);
                }

                DlReviews reviews = (DlReviews) Resources["Reviews"];
                reviews.Clear();
                foreach (DlReview x in allReviews)
                {
                    reviews.Add(x);
                }
            });
        }

        private async Task<(ConcurrentBag<DlQuestion>, ConcurrentBag<DlReview>)> ReloadAsync(string appName,string username)
        {
            MyBusinessService service = GetBusinessService(username, appName);

            ConcurrentBag<DlQuestion> allQuestions = new ConcurrentBag<DlQuestion>();
            ConcurrentBag<DlReview> allReviews = new ConcurrentBag<DlReview>();
            ConcurrentDictionary<string, Location> allLocations = new ConcurrentDictionary<string, Location>();

            List<Account> accountsFromService = await GetAccountsFromServiceAsync(service).ConfigureAwait(false);

            foreach (Account account in accountsFromService)
            {
                await Dispatcher.InvokeAsync(() => { LbAccounts.Items.Add(account.AccountName); });

                // Creates and executes the request.
                IEnumerable<Location> downloadedLocations = await GetStoresForAccountAsync(service, account).ConfigureAwait(false);

                await Task.WhenAll(downloadedLocations.Select(selectedLocation =>
                        ProcessLocationAsync(service, account, selectedLocation, allQuestions, allReviews, allLocations)))
                    .ConfigureAwait(false);
            }

            return (allQuestions, allReviews);
        }

        private async Task ProcessLocationAsync(MyBusinessService service, Account account, Location selectedLocation,
            ConcurrentBag<DlQuestion> allQuestions, ConcurrentBag<DlReview> allReviews,
            ConcurrentDictionary<string, Location> allLocations)
        {
            if (allLocations.TryGetValue(selectedLocation.StoreCode, out Location _))
            {
                await OutputAsync($"Already added {selectedLocation.StoreCode} to the responses.").ConfigureAwait(false);
            }
            else
            {
                allLocations.TryAdd(selectedLocation.StoreCode, selectedLocation);
                await OutputAsync($"Processing {selectedLocation.StoreCode}...").ConfigureAwait(false);

                await Dispatcher.InvokeAsync(() => { LbStores.Items.Add(selectedLocation.StoreCode); });

                await LookForUnImplementedUpdatesAsync(service, selectedLocation).ConfigureAwait(false);

                foreach (Question q in await GetStoreQuestionsAsync(service, selectedLocation).ConfigureAwait(false))
                {
                    allQuestions.Add(new DlQuestion(account, selectedLocation, q));
                }

                await OutputAsync($"Downloaded questions for {selectedLocation.LocationName}({selectedLocation.StoreCode})").ConfigureAwait(false);

                foreach (Review q in await GetStoreReviewsAsync(service, selectedLocation).ConfigureAwait(false))
                {
                    allReviews.Add(new DlReview(account, selectedLocation, q));
                }

                await OutputAsync($"Downloaded reviews for {selectedLocation.LocationName}({selectedLocation.StoreCode})").ConfigureAwait(false);
            }
        }

        private async Task LookForUnImplementedUpdatesAsync(MyBusinessService service, Location selectedLocation)
        {
            string updates = await GetUpdateStringForStoreAsync(service, selectedLocation).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(updates))
            {
                await AddUserNotificationAsync(
                    $"Google identified that {selectedLocation.LocationName}({selectedLocation.StoreCode}) has updates to be actioned ({updates})").ConfigureAwait(false);
            }
        }

        private async Task<string> GetUpdateStringForStoreAsync(MyBusinessService service, Location store)
        {
            AccountsResource.LocationsResource.GetGoogleUpdatedRequest updatesRequest =
                service.Accounts.Locations.GetGoogleUpdated(store.Name);

            GoogleUpdatedLocation updates = null;

            try
            {
                try
                {
                    updates = await updatesRequest.ExecuteAsync().ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    await OutputAsync($"Failed to get update records for {store.StoreCode}, retrying").ConfigureAwait(false);
                    try
                    {
                        updates = await updatesRequest.ExecuteAsync().ConfigureAwait(false);
                    }
                    catch (TaskCanceledException ex2)
                    {
                        await AddUserNotificationAsync(
                            $"PERMENANTLY FAILED to get update records for {store.StoreCode} {ex2.Message}");
                    }
                }
            }
            catch (Google.GoogleApiException ex)
            {
                await AddUserNotificationAsync($"Failed to get update records for {store.StoreCode} {ex.Message}");
            }

            return updates?.DiffMask?.ToString();
        }

        private async Task<List<Review>> GetStoreReviewsAsync(MyBusinessService service, Location store)
        {
            List<Review> reviews = new List<Review>();
            AccountsResource.LocationsResource.ReviewsResource.ListRequest selectedLocationReviews =
                service.Accounts.Locations.Reviews.List(store.Name);
            try
            {
                ListReviewsResponse reviewResult = await selectedLocationReviews.ExecuteAsync().ConfigureAwait(false);
                if (reviewResult != null)
                {
                    if (reviewResult.Reviews != null)
                    {
                        reviews.AddRange(reviewResult.Reviews);

                        while (reviewResult.NextPageToken != null)

                        {
                            selectedLocationReviews.PageToken = reviewResult.NextPageToken;

                            reviewResult = await selectedLocationReviews.ExecuteAsync().ConfigureAwait(false);

                            reviews.AddRange(reviewResult.Reviews);
                        }
                    }
                    else
                    {
                        await OutputAsync($"No reviews for {store.StoreCode}...");
                    }
                }
            }
            catch (Exception e)
            {
                await AddUserNotificationAsync($"Failed to get reviews for {store.StoreCode}... {e.Message}");
            }

            return reviews;
        }

        private async Task<List<Question>> GetStoreQuestionsAsync(MyBusinessService service, Location store)
        {
            List<Question> questions = new List<Question>();
            AccountsResource.LocationsResource.QuestionsResource.ListRequest selectedLocationQuestions =
                service.Accounts.Locations.Questions.List(store.Name);
            try
            {
                try
                {
                    await GetStoreQuestionsAsync(store, questions, selectedLocationQuestions).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    await OutputAsync($"Failed to get questions for {store.StoreCode}, retrying");
                    try
                    {
                        await GetStoreQuestionsAsync(store, questions, selectedLocationQuestions).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException ex2)
                    {
                        await AddUserNotificationAsync(
                            $"PERMENANTLY FAILED to get questions for {store.StoreCode} {ex2.Message}");
                    }
                }
            }
            catch (Google.GoogleApiException ex)
            {
                await AddUserNotificationAsync($"Failed to get questions for {store.StoreCode} {ex.Message}");
            }

            return questions;
        }

        private async Task GetStoreQuestionsAsync(Location store, List<Question> questions,
            AccountsResource.LocationsResource.QuestionsResource.ListRequest selectedLocationQuestions)
        {
            ListQuestionsResponse questionResult = await selectedLocationQuestions.ExecuteAsync().ConfigureAwait(false);
            if (questionResult != null)
            {
                if (questionResult.Questions != null)
                {
                    questions.AddRange(questionResult.Questions);

                    while (questionResult.NextPageToken != null)

                    {
                        selectedLocationQuestions.PageToken = questionResult.NextPageToken;

                        questionResult = await selectedLocationQuestions.ExecuteAsync().ConfigureAwait(false);

                        questions.AddRange(questionResult.Questions);
                    }
                }
                else
                {
                    await OutputAsync($"No questions found for {store.StoreCode}");
                }
            }
        }

        private static async Task<IEnumerable<Location>> GetStoresForAccountAsync(MyBusinessService service, Account account)
        {
            List<Location> downloadedLocations = new List<Location>();

            AccountsResource.LocationsResource.ListRequest locationsListRequest =
                service.Accounts.Locations.List(account.Name);

            locationsListRequest.PageSize = 100;

            ListLocationsResponse locationsResult = await locationsListRequest.ExecuteAsync().ConfigureAwait(false);

            if (locationsResult?.Locations != null)

            {
                downloadedLocations.AddRange(locationsResult.Locations);

                while (locationsResult.NextPageToken != null)

                {
                    locationsListRequest.PageToken = locationsResult.NextPageToken;

                    locationsResult = await locationsListRequest.ExecuteAsync().ConfigureAwait(false);

                    downloadedLocations.AddRange(locationsResult.Locations);
                }
            }

            else

            {
                Console.WriteLine($@"Account {account.Name} has no locations.");
            }

            return downloadedLocations;
        }

        private async Task<List<Account>> GetAccountsFromServiceAsync(MyBusinessService service)
        {
            List<Account> accounts = new List<Account>();

            try
            {
                ListAccountsResponse accountsResult = await service.Accounts.List().ExecuteAsync().ConfigureAwait(false);

                accounts.AddRange(accountsResult.Accounts);
            }
            catch (Exception e)
            {
                await OutputAsync(e.Message);
            }

            return accounts;
        }

        private static MyBusinessService GetBusinessService(string user, string appName)
        {
            ClientSecrets secrets = GetClientInformation("client_secrets.json");

            UserCredential credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                new[] {MybusinessServiceScope},
                user,
                CancellationToken.None,
                new FileDataStore("Mybusiness.Auth.Store")
            ).Result;

            // Creates the service.
            MyBusinessService service = new MyBusinessService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = appName,
            });
            return service;
        }
        private static ClientSecrets GetClientInformation(string secretFilename)
        {
            ClientSecrets secretsFile = null;
            try
            {
                using (FileStream stream = new FileStream(secretFilename, FileMode.Open, FileAccess.Read))
                {
                    secretsFile = GoogleClientSecrets.Load(stream).Secrets;
                }
            }
            catch (FileNotFoundException fnfe)
            {
                MessageBox.Show($"Cannot find secrets file at location: {fnfe.Message}",
                    "Secret connection details not found", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }

            return secretsFile;
        }

        /// <summary>
        /// Appends the given string to the on-screen log, and the debug console.
        /// </summary>
        /// <param name="output">string to be appended</param>
        private async Task OutputAsync(string output)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                TxtLog.AppendText(output + Environment.NewLine);
                TxtLog.ScrollToEnd();
            });

            Console.WriteLine(output);
        }

        private async Task AddUserNotificationAsync(string output)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                TxtUpdates.AppendText(output + Environment.NewLine);
                TxtUpdates.ScrollToEnd();
            });

            Console.WriteLine(output);
        }

        private void Refilter(object sender, RoutedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(GrdQuestion.ItemsSource).Refresh();
            CollectionViewSource.GetDefaultView(GrdReview.ItemsSource).Refresh();
        }

        private void QuestionCVS_Filter(object sender, FilterEventArgs e)
        {
            if (e.Item is DlQuestion t)
                // If filter is turned on, filter completed items.
            {
                DateTimeOffset gap = t.CreateTime;

                bool withinTimeFrame;
                switch (((ComboBoxItem) CmbQuestTf.SelectedItem)?.Content)
                {
                    case "Last Week":
                        withinTimeFrame = gap.AddDays(7) > DateTime.Now;
                        break;
                    case "Last 30 days":
                        withinTimeFrame = gap.AddDays(30) > DateTime.Now;
                        break;
                    case "Last 90 days":
                        withinTimeFrame = gap.AddDays(90) > DateTime.Now;
                        break;
                    case "Last Year":
                        withinTimeFrame = gap.AddDays(365) > DateTime.Now;
                        break;
                    case "All":
                        withinTimeFrame = true;
                        break;
                    case null:
                        withinTimeFrame = true;
                        break;
                    default:

                        throw new ArgumentException();
                }

                bool responseOk = (!(CbQuestNoResponse.IsChecked ?? false) ||
                                   ((CbQuestNoResponse.IsChecked ?? false) && t.TotalAnswerCount == 0));

                e.Accepted = (withinTimeFrame && responseOk);
            }
        }

        private void ReviewCVS_Filter(object sender, FilterEventArgs e)
        {
            if (e.Item is DlReview t)
                // If filter is turned on, filter completed items.
            {
                DateTimeOffset gap = t.CreateTime;

                bool withinTimeFrame;
                switch (((ComboBoxItem) CmbRevTf.SelectedItem)?.Content)
                {
                    case "Last Week":
                        withinTimeFrame = gap.AddDays(7) > DateTime.Now;
                        break;
                    case "Last 30 days":
                        withinTimeFrame = gap.AddDays(30) > DateTime.Now;
                        break;
                    case "Last 90 days":
                        withinTimeFrame = gap.AddDays(90) > DateTime.Now;
                        break;
                    case "Last Year":
                        withinTimeFrame = gap.AddDays(365) > DateTime.Now;
                        break;
                    case "All":
                        withinTimeFrame = true;
                        break;
                    case null:
                        withinTimeFrame = true;
                        break;
                    default:
                        throw new ArgumentException();
                }

                bool responseOk = (!(CbReviewNoResponse.IsChecked ?? false) ||
                                   ((CbReviewNoResponse.IsChecked ?? false) && string.IsNullOrWhiteSpace(t.Response)));

                bool commentsOk = (!(CbReviewNoComment.IsChecked ?? false) ||
                                   ((CbReviewNoComment.IsChecked ?? false) && !string.IsNullOrWhiteSpace(t.Comment)));

                e.Accepted = (withinTimeFrame && responseOk && commentsOk);
            }
        }

        private void RefilterCb(object sender, SelectionChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(GrdQuestion.ItemsSource).Refresh();
            CollectionViewSource.GetDefaultView(GrdReview.ItemsSource).Refresh();
        }

        private void WebPageClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Hyperlink link)
            {
                Process.Start(link.NavigateUri.AbsoluteUri);
            }
        }
    }
}