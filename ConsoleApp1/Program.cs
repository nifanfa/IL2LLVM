internal class Program
{
    public static int ForTest()
    {
        int i = 0;
        for (; i < 10; i++) ;
        return i;
    }

    public static int ArrayTest()
    {
        byte[] byteArr = new byte[4];
        byteArr[0] = 1;
        byteArr[1] = 2;
        byteArr[2] = 3;
        byteArr[3] = 4;

        short[] shortArr = new short[4];
        shortArr[0] = 5;
        shortArr[1] = 6;
        shortArr[2] = 7;
        shortArr[3] = 8;

        int[] intArr = new int[4];
        intArr[0] = 9;
        intArr[1] = 10;
        intArr[2] = 11;
        intArr[3] = 12;

        return
            byteArr[0] + byteArr[1] + byteArr[2] + byteArr[3] +
            shortArr[0] + shortArr[1] + shortArr[2] + shortArr[3] +
            intArr[0] + intArr[1] + intArr[2] + intArr[3];
    }

    public static void ObjectTest()
    {
        Echo echo = new Echo(123, true);
        echo.EchoMessage("Hello, Echo!");
        echo.EchoMessage("Test Store!");
        Console.Write("LastEcho is: ");
        Console.WriteLine(echo.LastEcho);
        echo.Test(1, 2, 3, 4);
    }

    public static int CalculateTest()
    {
        int a = 10;
        int b = 20;
        int d = 1024;
        return ((d * a) / b) / ((b * a));
    }

    public static void BranchTest()
    {
        int a = 10;
        int b = 20;

        if (a != b)
        {
            Console.WriteLine("Success1");
        }
        else
        {
            Console.WriteLine("Failed1");
        }
        if (a == b)
        {
            Console.WriteLine("Failed2");
        }
        else
        {
            Console.WriteLine("Success2");
        }
        if (a == b)
        {
            Console.WriteLine("Failed3");
        }
        if (a != b)
        {
            Console.WriteLine("Success3");
        }
        if (a > b)
        {
            Console.WriteLine("Failed4");
        }
        if (a < b)
        {
            Console.WriteLine("Success4");
        }
    }

    private static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
    }
}

class Echo
{
    public int Dummy1;
    public bool Dummy2;

    public string LastEcho;

    public Echo(int dummy1, bool dummy2)
    {
        Dummy1 = dummy1;
        Dummy2 = dummy2;
    }

    public void EchoMessage(string message)
    {
        Console.WriteLine(message);
        LastEcho = message;
    }

    public void Test(int a, int b, int c, int d) { }
}