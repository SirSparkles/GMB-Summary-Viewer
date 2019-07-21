using Google.Apis.Auth.OAuth2;
using Google.Apis.MyBusiness.v4;
using Google.Apis.MyBusiness.v4.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Linq;
using System.Windows.Data;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Documents;

namespace GetReviews
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public partial class MainWindow
    {
        // client configuration
        const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v3/userinfo";
        const string TokenRequestUri = "https://www.googleapis.com/oauth2/v4/token";

        // The scope of Google My Business API
        const string MybusinessServiceScope = "https://www.googleapis.com/auth/business.manage";


        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            await ReloadAsync(Name).ConfigureAwait(false);
        }

        private async Task ReloadAsync(string appName)
        {
            ClientSecrets secrets = GetClientInformation("client_secrets.json");

            (bool carryon, string username) = await Connect(secrets).ConfigureAwait(false);
            if (!carryon) return;

            MyBusinessService service = GetBusinessService(username, secrets, appName);

            ConcurrentBag<DlQuestion> allQuestions = new ConcurrentBag<DlQuestion>();
            ConcurrentBag<DlReview> allReviews = new ConcurrentBag<DlReview>();
            ConcurrentDictionary<string, Location> allLocations = new ConcurrentDictionary<string, Location>();

            await Dispatcher.InvokeAsync(() =>
            {
                LbAccounts.Items.Clear();
                LbStores.Items.Clear();
            });

            List<Account> accountsFromService = await GetAccountsFromService(service);

            foreach (Account account in accountsFromService)
            {
                await Dispatcher.InvokeAsync(() => { LbAccounts.Items.Add(account.AccountName); });

                // Creates and executes the request.
                List<Location> downloadedLocations = GetStoresForAccount(service, account);

                await Task.WhenAll(downloadedLocations.Select(selectedLocation =>
                        ProcessLocation(service, account, selectedLocation, allQuestions, allReviews, allLocations)))
                    .ConfigureAwait(false);
            }

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

        private async Task ProcessLocation(MyBusinessService service, Account account, Location selectedLocation,
            ConcurrentBag<DlQuestion> allQuestions, ConcurrentBag<DlReview> allReviews,
            ConcurrentDictionary<string, Location> allLocations)
        {
            if (allLocations.TryGetValue(selectedLocation.StoreCode, out Location _))
            {
                await Output($"Already added {selectedLocation.StoreCode} to the responses.");
            }
            else
            {
                allLocations.TryAdd(selectedLocation.StoreCode, selectedLocation);
                await Output($"Processing {selectedLocation.StoreCode}...");

                await Dispatcher.InvokeAsync(() => { LbStores.Items.Add(selectedLocation.StoreCode); });

                await LookForUnImplementedUpdates(service, selectedLocation);

                foreach (Question q in await GetStoreQuestions(service, selectedLocation))
                {
                    allQuestions.Add(new DlQuestion(account, selectedLocation, q));
                }

                await Output($"Downloaded questions for {selectedLocation.LocationName}({selectedLocation.StoreCode})");

                foreach (Review q in await GetStoreReviews(service, selectedLocation))
                {
                    allReviews.Add(new DlReview(account, selectedLocation, q));
                }

                await Output($"Downloaded reviews for {selectedLocation.LocationName}({selectedLocation.StoreCode})");
            }
        }

        private async Task LookForUnImplementedUpdates(MyBusinessService service, Location selectedLocation)
        {
            string updates = await GetUpdateStringForStore(service, selectedLocation);

            if (!string.IsNullOrWhiteSpace(updates))
            {
                await AddUserNotification(
                    $"Google identified that {selectedLocation.LocationName}({selectedLocation.StoreCode}) has updates to be actioned ({updates})");
            }
        }

        private async Task<string> GetUpdateStringForStore(MyBusinessService service, Location store)
        {
            AccountsResource.LocationsResource.GetGoogleUpdatedRequest updatesRequest =
                service.Accounts.Locations.GetGoogleUpdated(store.Name);

            GoogleUpdatedLocation updates = null;

            try
            {
                try
                {
                    updates = await updatesRequest.ExecuteAsync();
                }
                catch (TaskCanceledException)
                {
                    await Output($"Failed to get update records for {store.StoreCode}, retrying");
                    try
                    {
                        updates = await updatesRequest.ExecuteAsync();
                    }
                    catch (TaskCanceledException ex2)
                    {
                        await AddUserNotification(
                            $"PERMENANTLY FAILED to get update records for {store.StoreCode} {ex2.Message}");
                    }
                }
            }
            catch (Google.GoogleApiException ex)
            {
                await AddUserNotification($"Failed to get update records for {store.StoreCode} {ex.Message}");
            }

            return updates?.DiffMask?.ToString();
        }

        private async Task<List<Review>> GetStoreReviews(MyBusinessService service, Location store)
        {
            List<Review> reviews = new List<Review>();
            AccountsResource.LocationsResource.ReviewsResource.ListRequest selectedLocationReviews =
                service.Accounts.Locations.Reviews.List(store.Name);
            try
            {
                ListReviewsResponse reviewResult = selectedLocationReviews.Execute();
                if (reviewResult != null)
                {
                    if (reviewResult.Reviews != null)
                    {
                        reviews.AddRange(reviewResult.Reviews);

                        while (reviewResult.NextPageToken != null)

                        {
                            selectedLocationReviews.PageToken = reviewResult.NextPageToken;

                            reviewResult = selectedLocationReviews.Execute();

                            reviews.AddRange(reviewResult.Reviews);
                        }
                    }
                    else
                    {
                        await Output($"No reviews for {store.StoreCode}...");
                    }
                }
            }
            catch (Exception e)
            {
                await AddUserNotification($"Failed to get reviews for {store.StoreCode}... {e.Message}");
            }

            return reviews;
        }

        private async Task<List<Question>> GetStoreQuestions(MyBusinessService service, Location store)
        {
            List<Question> questions = new List<Question>();
            AccountsResource.LocationsResource.QuestionsResource.ListRequest selectedLocationQuestions =
                service.Accounts.Locations.Questions.List(store.Name);
            try
            {
                try
                {
                    await GetStoreQuestions(store, questions, selectedLocationQuestions);
                }
                catch (TaskCanceledException)
                {
                    await Output($"Failed to get questions for {store.StoreCode}, retrying");
                    try
                    {
                        await GetStoreQuestions(store, questions, selectedLocationQuestions);
                    }
                    catch (TaskCanceledException ex2)
                    {
                        await AddUserNotification(
                            $"PERMENANTLY FAILED to get questions for {store.StoreCode} {ex2.Message}");
                    }
                }
            }
            catch (Google.GoogleApiException ex)
            {
                await AddUserNotification($"Failed to get questions for {store.StoreCode} {ex.Message}");
            }

            return questions;
        }

        private async Task GetStoreQuestions(Location store, List<Question> questions,
            AccountsResource.LocationsResource.QuestionsResource.ListRequest selectedLocationQuestions)
        {
            ListQuestionsResponse questionResult = selectedLocationQuestions.Execute();
            if (questionResult != null)
            {
                if (questionResult.Questions != null)
                {
                    questions.AddRange(questionResult.Questions);

                    while (questionResult.NextPageToken != null)

                    {
                        selectedLocationQuestions.PageToken = questionResult.NextPageToken;

                        questionResult = selectedLocationQuestions.Execute();

                        questions.AddRange(questionResult.Questions);
                    }
                }
                else
                {
                    await Output($"No questions found for {store.StoreCode}");
                }
            }
        }

        private static List<Location> GetStoresForAccount(MyBusinessService service, Account account)
        {
            List<Location> downloadedLocations = new List<Location>();

            AccountsResource.LocationsResource.ListRequest locationsListRequest =
                service.Accounts.Locations.List(account.Name);

            locationsListRequest.PageSize = 100;

            ListLocationsResponse locationsResult = locationsListRequest.Execute();

            if (locationsResult?.Locations != null)

            {
                downloadedLocations.AddRange(locationsResult.Locations);

                while (locationsResult.NextPageToken != null)

                {
                    locationsListRequest.PageToken = locationsResult.NextPageToken;

                    locationsResult = locationsListRequest.Execute();

                    downloadedLocations.AddRange(locationsResult.Locations);
                }
            }

            else

            {
                Console.WriteLine($@"Account {account.Name} has no locations.");
            }

            return downloadedLocations;
        }

        private async Task<List<Account>> GetAccountsFromService(MyBusinessService service)
        {
            List<Account> accounts = new List<Account>();

            try
            {
                ListAccountsResponse accountsResult = service.Accounts.List().Execute();

                accounts.AddRange(accountsResult.Accounts);
            }
            catch (Exception e)
            {
                await Output(e.Message);
            }

            return accounts;
        }

        private static MyBusinessService GetBusinessService(string user, ClientSecrets secrets, string name)
        {
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
                ApplicationName = name,
            });
            return service;
        }

        private async Task<(bool, string)> Connect(ClientSecrets secrets)
        {
            // Generates state and PKCE values.
            string state = Net.RandomDataBase64Url(32);
            string codeVerifier = Net.RandomDataBase64Url(32);
            string codeChallenge = Net.Base64UrlencodeNoPadding(Net.Sha256(codeVerifier));
            const string codeChallengeMethod = "S256";

            // Creates a redirect URI using an available port on the loopback address.
            string redirectUri = $"http://{IPAddress.Loopback}:{Net.GetRandomUnusedPort()}/";
            await Output("redirect URI: " + redirectUri);

            // Creates an HttpListener to listen for requests on that redirect URI.
            HttpListener http = new HttpListener();
            http.Prefixes.Add(redirectUri);
            await Output("Listening..");
            http.Start();

            // Creates the OAuth 2.0 authorization request.
            string authorizationRequest =
                $"{AuthorizationEndpoint}?response_type=code&scope=openid%20profile&redirect_uri={Uri.EscapeDataString(redirectUri)}&client_id={secrets.ClientId}&state={state}&code_challenge={codeChallenge}&code_challenge_method={codeChallengeMethod}";

            // Opens request in the browser.
            Process.Start(authorizationRequest);

            // Waits for the OAuth authorization response.
            HttpListenerContext context = await http.GetContextAsync();

            // Brings this app back to the foreground.
            Activate();

            // Sends an HTTP response to the browser.
            HttpListenerResponse response = context.Response;
            const string responseString =
                "<html><head><meta http-equiv='refresh' content='10;url=https://google.com'></head><body>Please return to the app.</body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            Stream responseOutput = response.OutputStream;
            await responseOutput.WriteAsync(buffer, 0, buffer.Length).ContinueWith((task) =>
            {
                responseOutput.Close();
                http.Stop();
                Console.WriteLine(@"HTTP server stopped.");
            });

            // Checks for errors.
            if (context.Request.QueryString.Get("error") != null)
            {
                await Output($"OAuth authorization error: {context.Request.QueryString.Get("error")}.");
                return (false, string.Empty);
            }

            if (context.Request.QueryString.Get("code") == null
                || context.Request.QueryString.Get("state") == null)
            {
                await Output("Malformed authorization response. " + context.Request.QueryString);
                return (false, string.Empty);
            }

            // extracts the code
            string code = context.Request.QueryString.Get("code");
            string incomingState = context.Request.QueryString.Get("state");

            // Compares the receieved state to the expected value, to ensure that
            // this app made the request which resulted in authorization.
            if (incomingState != state)
            {
                await Output($"Received request with invalid state ({incomingState})");
                return (false, string.Empty);
            }

            await Output("Authorization code: " + code);

            // Starts the code exchange at the Token Endpoint.
            string username = await PerformCodeExchange(code, codeVerifier, redirectUri, secrets);
            return (true, username);
        }

        async Task<string> PerformCodeExchange(string code, string codeVerifier, string redirectUri,
            ClientSecrets secrets)
        {
            await Output("Exchanging code for tokens...");

            // builds the  request
            string tokenRequestBody =
                $"code={code}&redirect_uri={Uri.EscapeDataString(redirectUri)}&client_id={secrets.ClientId}&code_verifier={codeVerifier}&client_secret={secrets.ClientSecret}&scope=&grant_type=authorization_code";
            // sends the request
            HttpWebRequest tokenRequest = (HttpWebRequest) WebRequest.Create(TokenRequestUri);
            tokenRequest.Method = "POST";
            tokenRequest.ContentType = "application/x-www-form-urlencoded";
            tokenRequest.Accept = "Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            byte[] byteVersion = Encoding.ASCII.GetBytes(tokenRequestBody);
            tokenRequest.ContentLength = byteVersion.Length;
            Stream stream = tokenRequest.GetRequestStream();
            await stream.WriteAsync(byteVersion, 0, byteVersion.Length);
            stream.Close();

            try
            {
                // gets the response
                WebResponse tokenResponse = await tokenRequest.GetResponseAsync();
                Stream responseStream = tokenResponse.GetResponseStream();
                if (responseStream != null)
                {
                    using (StreamReader reader = new StreamReader(responseStream))
                    {
                        // reads response body
                        string responseText = await reader.ReadToEndAsync();
                        await Output(responseText);

                        // converts to dictionary
                        Dictionary<string, string> tokenEndpointDecoded =
                            JsonConvert.DeserializeObject<Dictionary<string, string>>(responseText);

                        string accessToken = tokenEndpointDecoded["access_token"];
                        string username = await UserinfoCall(accessToken);
                        return username;
                    }
                }
            }
            catch (WebException ex)
            {
                if (ex.Status == WebExceptionStatus.ProtocolError)
                {
                    if (ex.Response is HttpWebResponse response)
                    {
                        await Output("HTTP: " + response.StatusCode);
                        Stream responseStream = response.GetResponseStream();
                        if (responseStream != null)
                        {
                            using (StreamReader reader = new StreamReader(responseStream))
                            {
                                // reads response body
                                string responseText = await reader.ReadToEndAsync();
                                await Output(responseText);
                            }
                        }
                    }
                }
            }

            return string.Empty;
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

        async Task<string> UserinfoCall(string accessToken)
        {
            await Output("Making API Call to Userinfo...");

            // sends the request
            HttpWebRequest userinfoRequest = (HttpWebRequest) WebRequest.Create(UserInfoEndpoint);
            userinfoRequest.Method = "GET";
            userinfoRequest.Headers.Add($"Authorization: Bearer {accessToken}");
            userinfoRequest.ContentType = "application/x-www-form-urlencoded";
            userinfoRequest.Accept = "Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

            // gets the response
            WebResponse userinfoResponse = await userinfoRequest.GetResponseAsync();
            Stream responseStream = userinfoResponse.GetResponseStream();
            if (responseStream != null)
            {
                using (StreamReader userinfoResponseReader = new StreamReader(responseStream))
                {
                    // reads response body
                    string userinfoResponseText = await userinfoResponseReader.ReadToEndAsync();
                    await Output(userinfoResponseText);

                    Dictionary<string, string> tokenEndpointDecoded =
                        JsonConvert.DeserializeObject<Dictionary<string, string>>(userinfoResponseText);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        LblUserName.Content = tokenEndpointDecoded["name"];
                        ImgProfilePic.Source = new BitmapImage(new Uri(tokenEndpointDecoded["picture"]));
                    });
                    return tokenEndpointDecoded["name"];
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Appends the given string to the on-screen log, and the debug console.
        /// </summary>
        /// <param name="output">string to be appended</param>
        private async Task Output(string output)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                TxtLog.AppendText(output + Environment.NewLine);
                TxtLog.ScrollToEnd();
            });

            Console.WriteLine(output);
        }

        private async Task AddUserNotification(string output)
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