using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using BotCoreProxy;
using IronOcr;
using Keys = BotCoreProxy.Keys;

namespace TrashCounter
{
    class Program
    {
        private static int _amount;
        private static int _trashLastCheck;
        private static Thread _mainLoopThread;
        private static bool _needDispose;
        private static int _totalTrashCounter;
        private static DirectBitmapProxy _trashBoarderImg;
        private static bool _trashCoeffFound;

        public static void Main()
        {
            HookManager.AddInternalFunctionCallBack(InternalHooks.Post_CoreInitialize, typeof(Program).GetMethod("Post_CoreInitializeHook"));
            HookManager.AddInternalFunctionCallBack(InternalHooks.Post_Dispose, typeof(Program).GetMethod("Post_DisposeHook"));
        }

        public static bool Post_CoreInitializeHook(object[] args, out object funcResult)
        {
            try
            {
                CoreConfig.DisableInbuildTPH = true;
                _needDispose = false;
                _trashBoarderImg = new DirectBitmapProxy(new Bitmap("trashBoarder.png"));

                InitTrashIcon();

                if (_mainLoopThread == null || !_mainLoopThread.IsAlive)
                {
                    _mainLoopThread = new Thread(Start) // idk may be it need to be STA state
                    {
                        IsBackground = true
                    };
                    _mainLoopThread.Start();
                }
            }
            catch { }

            funcResult = null;
            return true;
        }
        public static bool Post_DisposeHook(object[] args, out object funcResult)
        {
            try
            {
                _needDispose = true;

                if (_mainLoopThread != null && _mainLoopThread.IsAlive)
                    _mainLoopThread.Join();

                _mainLoopThread = null;
            }
            catch { }

            funcResult = true;
            return true;
        }

        private static void Start()
        {
            while (!_needDispose)
            {
                try
                {
                    // get trash each second from screen
                    _amount = GetCurrentTrashCount();

                    if (_amount - _trashLastCheck <= 100 || (_amount - _trashLastCheck) >= 100)
                    {
                        // update only total trash when trash have increased since last timer tick
                        if (_amount > _trashLastCheck)
                        {
                            // get the delta of trash from last tick to this tick.
                            var deltaTrash = _amount - _trashLastCheck;

                            _totalTrashCounter += deltaTrash;

                            BotCore.TrashPerHour = _totalTrashCounter;
                        }
                    }

                    _trashLastCheck = _amount;
                }
                catch { }

                Thread.Sleep(2000);
            }
        }

        private static void InitTrashIcon()
        {
            TimeSpan span = TimeSpan.FromMilliseconds(200);
            var inventoryPos = ExternalConfig.Resolution.StartsWith("1920") ? new Point(1487, 344) : new Point(2125, 517); // TODO Fix for 2k resolution
            var trashCountPos = ExternalConfig.Resolution.StartsWith("1920") ? new Point(1531, 415) : new Point(0, 0); // TODO Fix for 2k resolution

            // check if inventory is open if not, open it.
            while (!Util.CheckInventory())
            {
                Input.KeyPress(Keys.I);
                Thread.Sleep(200);
            }

            // check if cursor is hidden
            if (!Mouse.CursorPresents())
            {
                // press ctrl to activate cursor if its hidden
                Mouse.SetCursor();
            }

            Mouse.MoveMouse(inventoryPos, span);
            Thread.Sleep(500);

            Input.MouseClickR();
            Thread.Sleep(500);

            Mouse.MoveMouse(trashCountPos, span);
            Thread.Sleep(500);

            Input.MouseClickL();
            Thread.Sleep(500);

            Util.CleanScreen();
        }

        private static int GetCurrentTrashCount()
        {
            var coeff = 20;

            // TODO Fix for 2k resolution
            int x = (ExternalConfig.Resolution.StartsWith("1920") ? 1200 : 1840);
            int y = 40;
            int x_ = (ExternalConfig.Resolution.StartsWith("1920") ? 1920 : 2560);
            int y_ = (ExternalConfig.Resolution.StartsWith("1920") ? 700 : 700);

            // Locate trash icon on screen
            var findTrashIcon = ImageWorker.GetDirectBitmap(x, y, x_, y_);

            //while (!_trashCoeffFound)
            //{
            //    if (ImageWorker.CV_ResolveImage(findTrashIcon.BaseBitmap, _trashBoarderImg.BaseBitmap, coeff).Length != 1)
            //    {
            //        coeff += 10;
            //    }

            //    _trashCoeffFound = true;
            //}

            var trashIcon =
                ImageWorker.CV_ResolveImage(findTrashIcon.BaseBitmap, _trashBoarderImg.BaseBitmap, 70);

            var test = trashIcon.First();

            findTrashIcon.Dispose();

            var correctedY = test.Y + 51 + y;
            var correctedX = test.X + x;
            
            // Read trash amount
            var trashIconRegion = ImageWorker.GetDirectBitmap(correctedX, correctedY, correctedX + 47, correctedY + 17);

            //// check output of croppedImg
            //trashIconRegion.BaseBitmap.Save("croppedImg.png");

            //var textFromImage = ImageWorker.CV_ExtractTextFromImage(trashIconRegion.BaseBitmap, 12, true, true);
            //var cleanedtxt = textFromImage.Replace("\n", "");
            //var result = Convert.ToInt32(cleanedtxt);


            OcrResult result;
            var ocr = new IronTesseract
            {
                Configuration =
                {
                    WhiteListCharacters = "0123456789"
                },
                Language = OcrLanguage.Financial
            };

            using (var input = new OcrInput(trashIconRegion))
            {
                //Input.EnhanceResolution(300);
                input.DeNoise();
                input.Invert();

                result = ocr.Read(input);
            }


            trashIconRegion.Dispose();

            return Convert.ToInt32(result.Text);
        }
    }
}
