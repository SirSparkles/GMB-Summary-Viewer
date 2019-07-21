using System;
using Google.Apis.MyBusiness.v4.Data;

namespace GetReviews
{
    public class DlReview
    {
        private readonly Account _account;
        private readonly Location _selectedLocation;
        private readonly Review _q;

        public DlReview(Account account, Location selectedLocation, Review q)
        {
            _account = account;
            _selectedLocation = selectedLocation;
            _q = q;
        }

        public string AccountName => _account.AccountName;
        public string LocationName => _selectedLocation.StoreCode;

        public string Url =>_selectedLocation.Metadata.MapsUrl;
        //https://www.google.com/search?q=Cash+Converters+ShellharbourNSW#lrd=0x6b13139f41cc6073:0x4c2ce1c9a46fb0b2,1,,,
        // Preferred option is to the dedicated pagfe, but can't get the right Id out $"https://business.google.com/reviews/l/{selectedLocation.???}/r/{q.ReviewId}";
        public DateTime CreateTime => DateTime.Parse(_q.CreateTime.ToString());
        public string StarRating => _q.StarRating;
        public string Comment => _q.Comment;
        public string Response => _q.ReviewReply?.Comment ?? string.Empty;
    }
}