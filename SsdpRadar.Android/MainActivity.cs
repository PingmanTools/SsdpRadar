using System;
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using Xamarin.Forms.Platform.Android;
using SsdpRadar.Forms;

namespace SsdpRadar.Android
{
   [Activity(Label = "SsdpRadar.Android", MainLauncher = true, Icon = "@drawable/icon")]
   public class MainActivity : FormsApplicationActivity
   {


      protected override void OnCreate(Bundle bundle)
      {
         base.OnCreate(bundle);

         Xamarin.Forms.Forms.Init(this, bundle);

         var networkInterfaceProvider = new NetworkInterfaceProvider();
         var finderService = new FinderService(networkInterfaceProvider);
         var radarApp = new RadarApp(finderService);
         LoadApplication(radarApp);

         // Set our view from the "main" layout resource
         //SetContentView(Resource.Layout.Main);

         // Get our button from the layout resource,
         // and attach an event to it
         //Button button = FindViewById<Button>(Resource.Id.MyButton);

         //button.Click += delegate { button.Text = string.Format("{0} clicks!", count++); };
      }


   }
}

