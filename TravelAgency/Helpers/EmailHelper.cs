using System.Net;
using System.Net.Mail;

namespace TravelAgency.Helpers
{
    public static class EmailHelper
    {
        public static void Send(string to, string subject, string body)
        {
            var fromAddress = new MailAddress("travelagency23454@gmail.com", "Travel Agency");
            var toAddress = new MailAddress(to);

            const string fromPassword = "erludkziqqrarmlz"; 

            var smtp = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromAddress.Address, fromPassword)
            };

            using var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = subject,
                Body = body
            };

            smtp.Send(message);
        }
    }
}
