using QinghuaStory;

/// <summary>
/// 记录最近一次「我的课程」拉取发起时所在的学期序号，供学期切换时展示成绩单。
/// </summary>
public static class MyCoursesSnapshotCache
{
    public static int LastRequestSemesterIndex { get; private set; } = -1;
    public static MyCoursesResponseV21 LastResponse { get; private set; }

    public static void Record(int semesterIndexWhenRequested, MyCoursesResponseV21 data)
    {
        if (data == null) return;
        LastRequestSemesterIndex = semesterIndexWhenRequested;
        LastResponse = data;
    }

    /// <summary>优先匹配与结束学期一致的快照，否则退回最近一次响应。</summary>
    public static MyCoursesResponseV21 ResolveForEndedSemester(int endedSemesterIndex)
    {
        if (LastResponse == null) return null;
        if (LastRequestSemesterIndex == endedSemesterIndex) return LastResponse;
        return LastResponse;
    }
}
