using Microsoft.CSharp;
using Microsoft.CSharp.RuntimeBinder;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DLRBinder = Microsoft.CSharp.RuntimeBinder.Binder;

namespace FastCallMethodsSample
{
    #region Sample class
    public interface ISampleGeneric { }

    class SampleGeneric<T> : ISampleGeneric
    {
        public long Process(T obj)
        {
            return String.Format("{0} [{1}]", obj.ToString(), obj.GetType().FullName).Length;
        }
    }

    class Container
    {
        private static Dictionary<Type, object> _instances = new Dictionary<Type, object>();

        public static void Register<T>(SampleGeneric<T> instance)
        {
            if (false == _instances.ContainsKey(typeof(T)))
            {
                _instances.Add(typeof(T), instance);
            }
            else
            {
                _instances[typeof(T)] = instance;
            }
        }

        public static SampleGeneric<T> Get<T>()
        {
            if (false == _instances.ContainsKey(typeof(T))) throw new KeyNotFoundException();
            return (SampleGeneric<T>)_instances[typeof(T)];
        }

        public static object Get(Type type)
        {
            if (false == _instances.ContainsKey(type)) throw new KeyNotFoundException();
            return _instances[type];
        }
    }
    #endregion

    struct TestResult
    {
        public long Result;
        public long ElapsedMilliseconds;
    }

    class Program
    {
        const int ITERATION_COUNT = 1000000;

        static void SimpleTest()
        {
            var arg = new DateTime(1961, 04, 12);
            Console.WriteLine("Прогрев");
            OutResult(TestDirectCall(arg));
            Console.WriteLine("Тест");
            Console.WriteLine();
            Console.WriteLine("Прямой вызов");
            OutResult(TestDirectCall(arg));
            Console.WriteLine("Вызов через отражение");
            OutResult(TestReflectionCall(arg));
            Console.WriteLine("Вызов через делегат");
            OutResult(TestDelegateCall(arg));
            Console.WriteLine("Вызов через делегат с оптимизациями");
            OutResult(TestDelegateOptimizeCall(arg));
            Console.WriteLine("Вызов через dynamic");
            OutResult(TestDynamicCall(arg));
        }

        static void StatTest(int countIterations)
        {
            var tests = new Dictionary<int, string>
            {
                {0, "Прямой вызов"},
                {1, "Вызов через отражение"},
                {2, "Вызов через делегат"},
                {3, "Вызов через делегат с оптимизациями"},
                {4, "Вызов через dynamic"}
            };
            var stats = new Dictionary<int, List<long>> 
            { 
                {0, new List<long>()},
                {1, new List<long>()},
                {2, new List<long>()},
                {3, new List<long>()},
                {4, new List<long>()}
            };
            var arg = new DateTime(1961, 04, 12);
            for (int i = 0; i < countIterations; i++)
            {
                TestDirectCall(arg);
                stats[0].Add(TestDirectCall(arg).ElapsedMilliseconds);

                TestReflectionCall(arg);
                stats[1].Add(TestReflectionCall(arg).ElapsedMilliseconds);

                TestDelegateCall(arg);
                stats[2].Add(TestDelegateCall(arg).ElapsedMilliseconds);

                TestDelegateOptimizeCall(arg);
                stats[3].Add(TestDelegateOptimizeCall(arg).ElapsedMilliseconds);

                TestDynamicCall(arg);
                stats[4].Add(TestDynamicCall(arg).ElapsedMilliseconds);
            }

            var text = new StringBuilder();
            for (int i = 0; i < stats.Count; i++)
            {
                var info = CalculateInfo(stats[i]);
                text.AppendLine(tests[i]);
                text.AppendLine("Min:\t" + info.Min.ToString() + " ms");
                text.AppendLine("Max:\t" + info.Max.ToString() + " ms");
                text.AppendLine("Mean:\t" + info.Mean.ToString() + " ms");
                text.AppendLine("Median:\t" + info.Median.ToString() + " ms");
                text.AppendLine();
            }
            Console.WriteLine(text.ToString());
            Clipboard.SetText(text.ToString());
        }

        static void OutResult(TestResult result)
        {
            Console.WriteLine("Result: " + result.Result);
            Console.WriteLine("Elapsed time: " + result.ElapsedMilliseconds);
            Console.WriteLine();
        }

        [STAThread]
        static void Main(string[] args)
        {
            Container.Register<DateTime>(new SampleGeneric<DateTime>());
            StatTest(10);
            Console.ReadKey();
        }

        public static TestResult TestDirectCall(DateTime arg)
        {
            var instance = Container.Get<DateTime>();
            Stopwatch sw = new Stopwatch();
            sw.Start();
            long summ = 0;
            for (long i = 0; i < ITERATION_COUNT; i++)
            {
                summ += instance.Process(arg);
            }
            sw.Stop();
            return new TestResult { Result = summ, ElapsedMilliseconds = sw.ElapsedMilliseconds };
        }
        
        public static TestResult TestReflectionCall(object arg)
        {
            var instance = Container.Get(arg.GetType());
            var method = instance.GetType().GetMethod("Process");
            Stopwatch sw = new Stopwatch();
            sw.Start();
            long summ = 0;
            for (long i = 0; i < ITERATION_COUNT; i++)
            {
                summ += (long)method.Invoke(instance, new object[] { arg });
            }
            sw.Stop();
            return new TestResult { Result = summ, ElapsedMilliseconds = sw.ElapsedMilliseconds };
        }

        public static TestResult TestDelegateCall(object arg)
        {
            var instance = Container.Get(arg.GetType());
            var hook = CreateDelegate(instance, instance.GetType().GetMethod("Process"));
            Stopwatch sw = new Stopwatch();
            sw.Start();
            long summ = 0;
            for (long i = 0; i < ITERATION_COUNT; i++)
            {
                summ += (long)hook.DynamicInvoke(arg);
            }
            sw.Stop();
            return new TestResult { Result = summ, ElapsedMilliseconds = sw.ElapsedMilliseconds };
        }

        public static TestResult TestDelegateOptimizeCall(object arg)
        {
            var instance = Container.Get(arg.GetType());
            var hook = CreateDelegate(instance, instance.GetType().GetMethod("Process"));
            Stopwatch sw = new Stopwatch();
            sw.Start();
            long summ = 0;
            for (long i = 0; i < ITERATION_COUNT; i++)
            {
                summ += (long)FastDynamicInvokeDelegate(hook, arg);
            }
            sw.Stop();
            return new TestResult { Result = summ, ElapsedMilliseconds = sw.ElapsedMilliseconds };
        }

        public static TestResult TestDynamicCall(dynamic arg)
        {
            var instance = Container.Get(arg.GetType());
            dynamic hook = CreateDynamic(instance, instance.GetType().GetMethod("Process"));
            Stopwatch sw = new Stopwatch();
            sw.Start();
            long summ = 0;
            for (long i = 0; i < ITERATION_COUNT; i++)
            {
                summ += hook(arg);
            }
            sw.Stop();
            return new TestResult { Result = summ, ElapsedMilliseconds = sw.ElapsedMilliseconds };
        }

        #region Helpers
        private static Delegate CreateDelegate(object target, MethodInfo method)
        {
            var methodParameters = method.GetParameters();
            var arguments = methodParameters.Select(d => Expression.Parameter(d.ParameterType, d.Name)).ToArray();
            var instance = target == null ? null : Expression.Constant(target);
            var methodCall = Expression.Call(instance, method, arguments);
            return Expression.Lambda(methodCall, arguments).Compile();
        }

        private static dynamic CreateDynamic(object target, MethodInfo method)
        {
            var methodParameters = method.GetParameters();
            var arguments = methodParameters.Select(d => Expression.Parameter(d.ParameterType, d.Name)).ToArray();
            var instance = target == null ? null : Expression.Constant(target);
            var methodCall = Expression.Call(instance, method, arguments);
            return Expression.Lambda(methodCall, arguments).Compile();
        }

        internal static object FastDynamicInvokeDelegate(Delegate del, params dynamic[] args)
        {
            dynamic tDel = del;
            switch (args.Length)
            {
                default:
                    try
                    {
                        return del.DynamicInvoke(args);
                    }
                    catch (TargetInvocationException ex)
                    {
                        throw ex.InnerException;
                    }
                #region Optimization
                case 1:
                    return tDel(args[0]);
                case 2:
                    return tDel(args[0], args[1]);
                case 3:
                    return tDel(args[0], args[1], args[2]);
                case 4:
                    return tDel(args[0], args[1], args[2], args[3]);
                case 5:
                    return tDel(args[0], args[1], args[2], args[3], args[4]);
                case 6:
                    return tDel(args[0], args[1], args[2], args[3], args[4], args[5]);
                case 7:
                    return tDel(args[0], args[1], args[2], args[3], args[4], args[5], args[6]);
                case 8:
                    return tDel(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7]);
                case 9:
                    return tDel(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8]);
                case 10:
                    return tDel(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9]);
                case 11:
                    return tDel(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10]);
                case 12:
                    return tDel(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11]);
                case 13:
                    return tDel(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12]);
                case 14:
                    return tDel(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12], args[13]);
                case 15:
                    return tDel(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12], args[13], args[14]);
                case 16:
                    return tDel(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12], args[13], args[14], args[15]);
                #endregion
            }
        }
        #endregion

        #region Statistics
        class StatInfo
        {
            public long Min;
            public long Max;
            public double Mean;
            public double Median;
        }

        private static StatInfo CalculateInfo(IEnumerable<long> array)
        {
            var sorted = array.OrderBy(n => n);
            double median;
            int halfIndex = sorted.Count() >> 1;
            if ((sorted.Count() % 2) == 0)
            {
                median = sorted.ElementAt(halfIndex);
                median += sorted.ElementAt(halfIndex - 1);
                median /= 2;
            }
            else
            {
                median = sorted.ElementAt(halfIndex);
            }
            return new StatInfo
            {
                Min = array.Min(),
                Max = array.Max(),
                Mean = array.Average(),
                Median = median
            };
        }
        #endregion
    }
}
