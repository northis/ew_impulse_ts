﻿using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.PatternGeneration;

namespace TradeKit.Tests;

public class PatternGenTests
{
    private PatternGenerator m_PatternGenerator;
    
    private static readonly string FOLDER_TO_SAVE = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "images");
    
    private static readonly ITimeFrame TIME_FRAME = TimeFrameHelper.Minute5;

    private static (DateTime, DateTime) GetDateRange(int barCount)
    {
        return Helper.GetDateRange(barCount, TIME_FRAME);
    }

    [SetUp]
    public void Setup()
    {
        m_PatternGenerator = new PatternGenerator(true);

        if (!Directory.Exists(FOLDER_TO_SAVE))
            Directory.CreateDirectory(FOLDER_TO_SAVE);
    }

    [Test]
    public void ExtendedFlatTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, dates.Item1, dates.Item2, TIME_FRAME)
                    {Min = 30}, ElliottModelType.FLAT_EXTENDED);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void DiagonalTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            //ModelPattern model = m_PatternGenerator.GetPattern(
            //    new PatternArgsItem(40, 60, dates.Item1, dates.Item2, m_TimeFrame), ElliottModelType.DIAGONAL_CONTRACTING_INITIAL);
            //SaveResultFiles(model);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, dates.Item1, dates.Item2, TIME_FRAME)
                    { Max = 70 }, ElliottModelType.DIAGONAL_CONTRACTING_ENDING);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void ExpandingDiagonalTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, dates.Item1, dates.Item2, TIME_FRAME), ElliottModelType.DIAGONAL_EXPANDING_INITIAL);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);

            model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(40, 60, dates.Item1, dates.Item2, TIME_FRAME), ElliottModelType.DIAGONAL_EXPANDING_ENDING);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void ZigzagTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            for (int j = 0; j < 10; j++)
            {
                (DateTime, DateTime) dates = GetDateRange(i);
                ModelPattern model = m_PatternGenerator.GetPattern(
                    new PatternArgsItem(60, 40, dates.Item1, dates.Item2, TIME_FRAME), ElliottModelType.ZIGZAG);
                ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
            }
        }
    }

    [Test]
    public void FlatTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, TIME_FRAME), ElliottModelType.FLAT_REGULAR);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void DoubleZigzagTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, TIME_FRAME), ElliottModelType.DOUBLE_ZIGZAG);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void TripleZigzagTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, TIME_FRAME), ElliottModelType.TRIPLE_ZIGZAG);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void TriangleTest()
    {
        for (int i = 100; i <= 100; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(70, 90, dates.Item1, dates.Item2, TIME_FRAME)
                    {Max = 120}, ElliottModelType.TRIANGLE_CONTRACTING);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void TriangleRunningTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(70, 90, dates.Item1, dates.Item2, TIME_FRAME)
                    { Max = 120, Min = 50}, ElliottModelType.TRIANGLE_RUNNING);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void TriangleExpandingTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(70, 90, dates.Item1, dates.Item2, TIME_FRAME)
                    { Min = 50 }, ElliottModelType.TRIANGLE_EXPANDING);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void CombinationTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(70, 90, dates.Item1, dates.Item2, TIME_FRAME) 
                    { Min = 50, Max = 110 }, ElliottModelType.COMBINATION);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void RunningFlatTest()
    {
        for (int i = 15; i <= 15; i++)
        {
            (DateTime, DateTime) dates = GetDateRange(i);
            ModelPattern model = m_PatternGenerator.GetPattern(
                new PatternArgsItem(60, 40, dates.Item1, dates.Item2, TIME_FRAME)
                    {Max = 66, Min = 34}, ElliottModelType.FLAT_RUNNING);
            ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE);
        }
    }

    [Test]
    public void ImpulseTest()
    {
        (DateTime, DateTime) dates = GetDateRange(15);

        PatternArgsItem paramArgs = new PatternArgsItem(
            40, 60, dates.Item1, dates.Item2, TIME_FRAME);
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.IMPULSE, true);
        ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE, model.Level);
    }

    [Test]
    public void SimpleImpulseTest()
    {
        (DateTime, DateTime) dates = GetDateRange(15);

        PatternArgsItem paramArgs = new PatternArgsItem(
            40, 60, dates.Item1, dates.Item2, TIME_FRAME);
        ModelPattern model = m_PatternGenerator.GetPattern(
            paramArgs, ElliottModelType.SIMPLE_IMPULSE, true);
        ChartGenerator.SaveResultFiles(model, FOLDER_TO_SAVE, model.Level);
    }
}