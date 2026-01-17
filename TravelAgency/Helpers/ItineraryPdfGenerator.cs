using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TravelAgency.ViewModel;

namespace TravelAgency.Helpers;

public static class ItineraryPdfGenerator
{
    public static byte[] Generate(BookingViewModel booking)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(12));

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Travel Agency").FontSize(20).SemiBold().FontColor(Colors.Blue.Darken2);
                        col.Item().Text("Trip Itinerary").FontSize(14).SemiBold();
                    });

                    row.ConstantItem(180).AlignRight().Column(col =>
                    {
                        col.Item().Text($"Booking #{booking.BookingId}").SemiBold();
                        col.Item().Text($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(10).FontColor(Colors.Grey.Darken1);
                    });
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    var title = !string.IsNullOrWhiteSpace(booking.PackageName)
                        ? booking.PackageName
                        : $"{booking.Destination}, {booking.Country}";

                    col.Item().Text(title).FontSize(16).SemiBold();

                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(140);
                            c.RelativeColumn();
                        });

                        void Row(string label, string value)
                        {
                            t.Cell().PaddingVertical(4).Text(label).SemiBold().FontColor(Colors.Grey.Darken2);
                            t.Cell().PaddingVertical(4).Text(value);
                        }

                        Row("Destination", $"{booking.Destination}, {booking.Country}");
                        Row("Dates", $"{booking.StartDate:dd/MM/yyyy} - {booking.EndDate:dd/MM/yyyy}");
                        Row("Category", booking.Category ?? "-");
                        Row("Rooms", booking.Quantity.ToString());
                        Row("Price per room", $"¤{booking.Price:0.00}");
                        Row("Booking status", booking.Status);
                        Row("Paid", booking.IsPaid ? "Yes" : "No");
                    });

                    if (!string.IsNullOrWhiteSpace(booking.Description))
                    {
                        col.Item().PaddingTop(6).Text("Description").FontSize(13).SemiBold();
                        col.Item().Text(booking.Description).FontColor(Colors.Grey.Darken3);
                    }

                    col.Item().PaddingTop(10).Text("Notes").FontSize(13).SemiBold();
                    col.Item().Text("• Please arrive on time for departure.\n• Bring a valid ID/passport (if required).\n• For changes or questions, contact Travel Agency support.")
                        .FontColor(Colors.Grey.Darken3);
                });

                page.Footer()
                    .AlignCenter()
                    .Text("Travel Agency • This itinerary is generated automatically.")
                    .FontSize(10)
                    .FontColor(Colors.Grey.Darken1);
            });
        }).GeneratePdf();
    }
}
