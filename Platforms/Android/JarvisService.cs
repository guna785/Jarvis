using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Java.Interop;
using Microsoft.Maui.Platform;
using System;
using System.Collections.Generic;
using System.Text;
using Color = Android.Graphics.Color;
using View = Android.Views.View;

namespace Jarvis.Platforms.Android
{  

    [Service(Enabled = true, Exported = false)]
    public class JarvisService : Service
    {
        private IWindowManager _windowManager;
        private View _nativeView;


        public override IBinder? OnBind(Intent? intent) => null;

        public override void OnCreate()
        {
            base.OnCreate();
            _windowManager = GetSystemService(WindowService).JavaCast<IWindowManager>();

            var mauiView = new Jarvis.Controls.JarvisHudView();
            var mauiContext = Microsoft.Maui.Controls.Application.Current.Handler.MauiContext;
            _nativeView = mauiView.ToPlatform(mauiContext);

            // FIX CS0103: Use WindowManagerFlags enum
            var flags = WindowManagerFlags.NotTouchModal |
                        WindowManagerFlags.WatchOutsideTouch |
                        WindowManagerFlags.LayoutInScreen |
                        WindowManagerFlags.NotFocusable;

            // FIX CS1739 & CS0117: Use the positional constructor 
            // Constructor: (width, height, type, flags, format)
            var lp = new WindowManagerLayoutParams(
                WindowManagerLayoutParams.MatchParent, // width
                900,                                   // height
                WindowManagerTypes.ApplicationOverlay, // type (FIX CS0117)
                flags,                                 // flags
                Format.Translucent                     // format
            );

            lp.Gravity = GravityFlags.Top;

            _nativeView.SetBackgroundColor(Color.Transparent);
            _windowManager.AddView(_nativeView, lp);
        }

        public override void OnDestroy()
        {
            if (_nativeView != null) _windowManager.RemoveView(_nativeView);
            base.OnDestroy();
        }
    }
}
