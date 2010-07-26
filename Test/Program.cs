using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SharpUPnP;

namespace Test
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(UPnP.Discover());

            Console.ReadLine();
        }
    }
}
