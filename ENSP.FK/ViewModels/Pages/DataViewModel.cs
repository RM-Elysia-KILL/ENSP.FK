using ENSP.FK.Models;
using System.Windows.Media;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.FK.ViewModels.Pages
{
    public partial class DataViewModel : ObservableObject, INavigationAware
    {
        [ObservableProperty]
        private IEnumerable<DataColor> _colors;

        public Task OnNavigatedToAsync()
        {
            RefreshData();
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void RefreshData()
        {
            var random = new Random();
            var colorCollection = new List<DataColor>();

            for (int i = 0; i < 8192; i++)
                colorCollection.Add(
                    new DataColor
                    {
                        Color = new SolidColorBrush(
                            Color.FromArgb(
                                (byte)200,
                                (byte)random.Next(0, 250),
                                (byte)random.Next(0, 250),
                                (byte)random.Next(0, 250)
                            )
                        )
                    }
                );

            Colors = colorCollection;
        }
    }
}
