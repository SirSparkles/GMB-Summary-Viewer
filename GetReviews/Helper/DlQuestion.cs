using System;
using Google.Apis.MyBusiness.v4.Data;

namespace GetReviews
{
    public class DlQuestion
    {
        private readonly Account _account;
        private readonly Location _selectedLocation;
        private readonly Question _q;

        public DlQuestion(Account account, Location selectedLocation, Question q)
        {
            _account = account;
            _selectedLocation = selectedLocation;
            _q = q;
        }

        public string AccountName => _account.AccountName;
        public string LocationName => _selectedLocation.StoreCode;
        public DateTime CreateTime => DateTime.Parse(_q.CreateTime.ToString());

        public string Url =>
            $"https://www.google.com/search?q={_selectedLocation.LocationName}%20{_selectedLocation.StoreCode}#lpqa=d,2"; //selectedLocation.Metadata.MapsUrl;

        public int? UpvoteCount => _q.UpvoteCount;
        public string Text => _q.Text;
        public int TotalAnswerCount => _q.TotalAnswerCount ?? 0;
    }
}