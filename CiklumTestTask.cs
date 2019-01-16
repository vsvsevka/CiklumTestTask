public delegate object ExecutingFunction(object inputData, CancellationToken cancellationToken);

    public class TaskData
    {
        public object Input { get; set; }
        public object Output { get; set; }
        public ExecutingFunction Function { get; set; }
        public string Error { get; set; }
    }

    public class ExecutionSequence
    {
        // Max timeout task can be executed to prevent hanging
        private int _timeout;

        // Max amount of tasks to handle fast-filling situations
        private int _maxTasksNumber;

        // Tasks collection, producing-consuming sequence
        private BlockingCollection<TaskData> _tasksSequence;

        private bool _isSequenceStopped;

        // Sync object to provide thread safety for Start/Stop operations
        private object _isSequenceOperationInProgress;
        private Task _currentTask;
        private CancellationToken _token;
        private CancellationTokenSource _tokenSource;

        public ExecutionSequence(int timeout, int maxTasksNumber)
        {
            _timeout = timeout;
            _isSequenceStopped = false;
            _maxTasksNumber = maxTasksNumber;
            _isSequenceOperationInProgress = false;
            _tokenSource = new CancellationTokenSource();
            _token = _tokenSource.Token;
            _tasksSequence = new BlockingCollection<TaskData>(_maxTasksNumber);
        }

        public void Start()
        {
            lock (_isSequenceOperationInProgress)
            {
                _isSequenceStopped = false;

                if (_currentTask == null || _currentTask.IsCompleted)
                {
                    _currentTask = Task.Factory.StartNew(() =>
                    {
                        while (!_isSequenceStopped && !_token.IsCancellationRequested)
                        {
                            TaskData taskFunction = null;
                            try
                            {
                                _tasksSequence.TryTake(out taskFunction);
                                if (taskFunction != null && taskFunction.Function != null)
                                {
                                    Task.Factory.StartNew(() =>
                                        taskFunction.Function.Invoke(taskFunction.Input, _token)).Wait(_timeout);
                                }
                            }
                            catch (Exception ex)
                            {
                                // Write error for current task and take next task for execution
                                taskFunction.Error = ex.Message;
                            }
                        }
                    }, _token);
                }
            }
        }

        public void Stop()
        {
            lock (_isSequenceOperationInProgress)
            { 
                _isSequenceStopped = true;

                // Cancel task being executed if such is present
                if (_currentTask != null)
                {
                    _tokenSource.Cancel();
                }
            }
        }

        public bool TryAddTask(TaskData task)
        {
            if (_maxTasksNumber > _tasksSequence.Count)
            {
                return _tasksSequence.TryAdd(task);
            }
            return false;
        }

        public void AddTask(TaskData task)
        {
            if (_maxTasksNumber > _tasksSequence.Count)
            {
                _tasksSequence.Add(task);
            }
        }
    }