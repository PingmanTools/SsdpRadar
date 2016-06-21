using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Xamarin.Forms;

namespace SsdpRadar.Forms
{
   public class RadarApp : Application
   {
      IFinderService finderService;
      StackLayout stackLayout;

      public RadarApp(IFinderService finderService)
      {
         this.finderService = finderService;
         finderService.DeviceFound += FinderService_DeviceFound;

         stackLayout = new StackLayout
         {
            VerticalOptions = LayoutOptions.Start
         };
         var scrollView = new ScrollView { Content = stackLayout };

         // The root page of your application
         MainPage = new ContentPage
         {
            Content = scrollView
         };
      }

      private void FinderService_DeviceFound(SsdpDevice device)
      {
         Device.BeginInvokeOnMainThread(() =>
         {
            stackLayout.Children.Add(new Label
            {
               XAlign = TextAlignment.Center,
               Text = device.Info.FriendlyName
            });
         });
      }

      protected override void OnStart()
      {
         finderService.Start();
      }

      protected override void OnSleep()
      {
         // Handle when your app sleeps
      }

      protected override void OnResume()
      {
         // Handle when your app resumes
      }
   }
}
