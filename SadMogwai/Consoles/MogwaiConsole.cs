﻿using Microsoft.Xna.Framework;
using SadConsole;
using SadConsole.Surfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SadMogwai.Consoles
{
    public class MogwaiConsole : SadConsole.Console
    {
        Basic borderSurface;

        public MogwaiConsole(string title, string footer, int width, int height) : base(width, height)
        {
            Cursor.Position = new Point(0, 0);
   
            borderSurface = new Basic(width + 2, height + 2, base.Font);
            borderSurface.DrawBox(new Rectangle(0, 0, borderSurface.Width, borderSurface.Height), 
                                  new Cell(Color.DarkCyan, Color.Black), null, SurfaceBase.ConnectedLineThick);
            borderSurface.Position = new Point(-1, -1);
            borderSurface.Print(2, 0, title, Color.DarkCyan, Color.Black);
            borderSurface.Print(width - footer.Length - 2, height + 1, footer, Color.DarkCyan, Color.Black);
            Children.Add(borderSurface);

            
        }

    }
}
