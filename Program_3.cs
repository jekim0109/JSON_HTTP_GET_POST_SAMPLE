using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SP_TEST
{
    /// <summary>
    /// 문제 3: HTTP API 기반 정확도 산출 서비스
    /// MONITORING.TXT 파일 대신 HTTP API를 통해 모니터링 데이터를 수신하고,
    /// In-Memory 저장소에서 AI 모델별 정확도를 조회하는 웹 서비스를 제공합니다.
    /// 
    /// 주요 기능:
    /// - MODELS.JSON 파일 로드: 모델명별 AgentId 매핑 정보 읽기
    /// - POST /monitoring: 모니터링 데이터(JSON) 수신 및 저장
    /// - GET /accuracy?modelName=&timeWindow=: AI 모델 정확도 조회 API
    /// </summary>
    class Program
    {
        // ==================== 전역 데이터 저장소 ====================
        
        /// <summary>
        /// HTTP를 통해 수신된 모니터링 데이터를 저장하는 리스트
        /// 스레드 안전성을 위해 lockObj를 사용하여 동기화합니다.
        /// </summary>
        static List<MonitoringData> monitoringDataStore = new List<MonitoringData>();
        
        /// <summary>
        /// 모델명(key)과 해당 모델의 AgentId 목록(value)을 매핑하는 딕셔너리
        /// MODELS.JSON 파일에서 로드된 데이터를 저장합니다.
        /// 예: {"modelA": ["agent1", "agent2"], "modelB": ["agent3"]}
        /// </summary>
        static Dictionary<string, List<string>> modelsInfo = new Dictionary<string, List<string>>();
        
        /// <summary>
        /// 모니터링 데이터 저장소 접근 시 사용하는 동기화용 객체
        /// 여러 스레드가 동시에 데이터에 접근하는 것을 방지합니다.
        /// </summary>
        static object lockObj = new object();

        /// <summary>
        /// 프로그램 진입점
        /// MODELS.JSON 파일을 로드하고 HTTP 서버를 시작합니다.
        /// </summary>
        /// <param name="args">명령줄 인수 (사용되지 않음)</param>
        static void Main(string[] args)
        {
            // MODELS.JSON 파일 로드: 모델명별 AgentId 매핑 정보 읽기
            LoadModelsInfo();

            // HTTP 서버 설정: localhost 포트 5100에서 리스닝
            var listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:5100/");
            listener.Start();
            
            Console.WriteLine("Server started on http://localhost:5100/");
            
            // 비동기로 HTTP 요청 수신 (프로그램이 종료되지 않도록 백그라운드에서 실행)
            Task.Run(() => ListenForRequests(listener));
            
            // 프로그램이 계속 실행되도록 콘솔 입력 대기
            Console.ReadLine();
        }

        /// <summary>
        /// MODELS.JSON 파일에서 모델 정보를 로드합니다.
        /// JSON 형식: {"modelName": ["agentId1", "agentId2"], ...}
        /// 파일이 없거나 파싱에 실패해도 에러 없이 빈 상태로 시작합니다.
        /// </summary>
        static void LoadModelsInfo()
        {
            // MODELS.JSON 파일 경로 설정 (프로그램 실행 위치 기준 상대경로)
            string modelsFile = Path.Combine(Directory.GetCurrentDirectory(), "MODELS.JSON");
            
            // 파일이 존재하는지 확인
            if (File.Exists(modelsFile))
            {
                // 파일 내용 읽기
                string json = File.ReadAllText(modelsFile);
                // System.Text.Json 라이브러리를 사용하여 JSON 파싱
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    // JSON의 각 필드(모델명)를 순회합니다.
                    foreach (var prop in root.EnumerateObject())
                    {
                        string modelName = prop.Name;    // 모델명 (예: "modelA")
                        var agentIds = new List<string>(); // 해당 모델의 AgentId 목록
                        
                        // 배열 내의 각 AgentId를 추출합니다.
                        foreach (var agentId in prop.Value.EnumerateArray())
                        {
                            agentIds.Add(agentId.GetString());
                        }
                        
                        // 딕셔너리에 저장: modelName -> agentIds
                        modelsInfo[modelName] = agentIds;
                    }
                }
                catch
                {
                    // JSON 파싱 실패 시 빈 값 사용 (에러 무시)
                }
            }
        }

        /// <summary>
        /// HTTP 요청을 비동기로 수신하고 처리합니다.
        /// 클라이언트가 연결될 때까지 대기하다가 도착한 요청을 HandleRequest로 전달합니다.
        /// </summary>
        static void ListenForRequests(HttpListener listener)
        {
            // 서버가 종료될 때까지 무한히 요청을 수신합니다.
            while (true)
            {
                try
                {
                    // 클라이언트의 요청을 대기 (블로킹 호출)
                    var context = listener.GetContext();
                    // 각 요청을 백그라운드 태스크로 처리 (동시 다중 요청 지원)
                    Task.Run(() => HandleRequest(context));
                }
                catch
                {
                    // 리스너가 중지되면 루프를 탈출합니다.
                    break;
                }
            }
        }

        /// <summary>
        /// 단일 HTTP 요청을 처리합니다.
        /// 요청 경로에 따라 모니터링 데이터 수신 또는 정확도 조회를 처리합니다.
        /// </summary>
        static void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response();
            
            try
            {
                // POST /monitoring: 모니터링 데이터 수신 엔드포인트
                if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/monitoring")
                {
                    // 요청 본문(JSON)을 읽어옵니다.
                    using (var reader = new StreamReader(request.InputStream))
                    {
                        string json = reader.ReadToEnd();
                        // 수신된 모니터링 데이터를 처리합니다.
                        ProcessMonitoringData(json);
                    }
                    
                    // 200 OK 응답 반환
                    byte[] buffer = Encoding.UTF8.GetBytes("OK");
                    response.StatusCode = 200;
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                // GET /accuracy: 정확도 조회 엔드포인트
                else if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/accuracy")
                {
                    // URL 쿼리 파라미터에서 modelName과 timeWindow 추출
                    string queryString = request.Url.Query;
                    string modelName = GetQueryParam(queryString, "modelName");
                    string timeWindow = GetQueryParam(queryString, "timeWindow");
                    
                    // 필수 파라미터가 누락된 경우 400 에러 반환
                    if (string.IsNullOrEmpty(modelName) || string.IsNullOrEmpty(timeWindow))
                    {
                        response.StatusCode = 400;
                        byte[] errorBuffer = Encoding.UTF8.GetBytes("modelName과 timeWindow 파라미터 필수");
                        response.ContentLength64 = errorBuffer.Length;
                        response.OutputStream.Write(errorBuffer, 0, errorBuffer.Length);
                    }
                    else
                    {
                        // 정확도 계산 수행 및 결과 반환
                        string result = CalculateAccuracy(modelName, timeWindow);
                        byte[] resultBuffer = Encoding.UTF8.GetBytes(result);
                        response.StatusCode = 200;
                        response.ContentLength64 = resultBuffer.Length;
                        response.OutputStream.Write(resultBuffer, 0, resultBuffer.Length);
                    }
                }
                // 지원하지 않는 경로인 경우 404 반환
                else
                {
                    response.StatusCode = 404;
                }
            }
            catch (Exception ex)
            {
                // 예외 발생 시 500 에러 반환
                response.StatusCode = 500;
                byte[] errorBuffer = Encoding.UTF8.GetBytes(ex.Message);
                response.ContentLength64 = errorBuffer.Length;
                response.OutputStream.Write(errorBuffer, 0, buffer.Length);
            }
            finally
            {
                // 응답 스트림 닫기
                response.OutputStream.Close();
            }
        }

        /// <summary>
        /// 수신된 모니터링 JSON 데이터를 파싱하여 저장소 추가합니다.
        /// 단일 객체 또는 배열 형식 모두 지원합니다.
        /// </summary>
        static void ProcessMonitoringData(string json)
        {
            // 스레드 안전성을 위해 lock으로 보호
            lock (lockObj)
            {
                try
                {
                    // JSON 파싱
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    // 배열 형식인 경우: 각 항목을 개별 데이터로 처리
                    if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray())
                        {
                            var data = new MonitoringData
                            {
                                RequestId = GetProperty(item, "requestId"),
                                Timestamp = GetProperty(item, "timestamp"),
                                AgentId = GetProperty(item, "agentId"),
                                DataType = GetProperty(item, "dataType"),
                                DataValue = GetProperty(item, "dataValue")
                            };
                            monitoringDataStore.Add(data);
                        }
                    }
                    // 단일 객체 형식인 경우
                    else if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        var data = new MonitoringData
                        {
                            RequestId = GetProperty(root, "requestId"),
                            Timestamp = GetProperty(root, "timestamp"),
                            AgentId = GetProperty(root, "agentId"),
                            DataType = GetProperty(root, "dataType"),
                            DataValue = GetProperty(root, "dataValue")
                        };
                        monitoringDataStore.Add(data);
                    }
                }
                catch
                {
                    // JSON 파싱 실패 시 무시 (에러 없이 silently fail)
                }
            }
        }

        /// <summary>
        /// 특정 모델의 AI 정확도를 계산합니다.
        /// MODELS.JSON에서 모델의 AgentId를 가져오고, 해당 에이전트의 데이터를 필터링하여 정확도를 계산합니다.
        /// </summary>
        /// <param name="modelName">조회할 모델명</param>
        /// <param name="timeWindow">시간 범위 (yyyyMMddHH 형식)</param>
        /// <returns>"정확도/총계산수" 형식의 결과 문자열 (예: "85/100")</returns>
        static string CalculateAccuracy(string modelName, string timeWindow)
        {
            // 스레드 안전성을 위해 lock으로 보호
            lock (lockObj)
            {
                // 모델명이 modelsInfo에 없는 경우: 0/0 반환
                if (!modelsInfo.ContainsKey(modelName))
                {
                    return "0/0";
                }
                
                // 해당 모델의 AgentId 목록 가져오기
                var agentIds = modelsInfo[modelName];
                
                // 시간 범위 파싱: yyyyMMddHH 형식을 시작/끝 Timestamp로 변환
                int year = int.Parse(timeWindow.Substring(0, 4));
                int month = int.Parse(timeWindow.Substring(4, 2));
                int day = int.Parse(timeWindow.Substring(6, 2));
                int hour = int.Parse(timeWindow.Substring(8, 2));
                
                // 시작 Timestamp: 해당 시간대의 첫 번째 시점 (00분 00초 00밀리초)
                string startTimestamp = $"{year:0000}{month:00}{day:00}{hour:00}0000";
                // 끝 Timestamp: 해당 시간대의 마지막 시점 (59분 59초 59밀리초)
                string endTimestamp = $"{year:0000}{month:00}{day:00}{hour:00}5959";
                
                // 해당 모델의 데이터만 필터링하여 임시 저장소 생성
                // 구조: Dictionary<요청ID, Dictionary<데이터타입('P'/'A'), 값>>
                var dataMap = new Dictionary<string, Dictionary<string, string>>();
                
                // 전체 모니터링 데이터에서 해당 모델의 에이전트 데이터만 추출
                foreach (var data in monitoringDataStore)
                {
                    // 현재 데이터의 AgentId가 대상 모델의 에이전트 목록에 없으면 건너뜁니다.
                    if (!agentIds.Contains(data.AgentId))
                        continue;
                    
                    // P(예측 값) 데이터인 경우, 해당 데이터의 Timestamp가 시간 범위 내에 있는지 확인
                    if (data.DataType == "P")
                    {
                        if (int.Parse(data.Timestamp) < int.Parse(startTimestamp) || int.Parse(data.Timestamp) > int.Parse(endTimestamp))
                            continue;
                    }
                    
                    // 요청ID별로 데이터를 그룹화합니다.
                    if (!dataMap.ContainsKey(data.RequestId))
                    {
                        dataMap[data.RequestId] = new Dictionary<string, string>();
                    }
                    
                    // 데이터 타입('P' 또는 'A')을 키로 값 저장
                    dataMap[data.RequestId][data.DataType] = data.DataValue;
                }
                
                // 정확도 계산: 총 개수와 예측값(P)과 실제값(A)이 일치하는 수
                int total = dataMap.Count;
                int correct = 0;
                
                foreach (var kvp in dataMap)
                {
                    string pValue = kvp.Value.ContainsKey("P") ? kvp.Value["P"] : "";
                    string aValue = kvp.Value.ContainsKey("A") ? kvp.Value["A"] : "";
                    
                    // 예측값과 실제값이 일치하면 correct 카운트 증가
                    if (pValue == aValue)
                    {
                        correct++;
                    }
                }
                
                // "correct/total" 형식의 결과 반환
                return $"{correct}/{total}";
            }
        }

        /// <summary>
        /// JsonElement에서 특정 속성 값을 가져옵니다.
        /// 속성이 존재하지 않으면 빈 문자열을 반환합니다.
        /// </summary>
        static string GetProperty(System.Text.Json.JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out System.Text.Json.JsonElement value))
            {
                return value.GetString() ?? "";
            }
            return "";
        }

        /// <summary>
        /// URL 쿼리 문자열에서 특정 파라미터의 값을 추출합니다.
        /// URL 디코딩도 함께 수행합니다.
        /// </summary>
        static string GetQueryParam(string queryString, string paramName)
        {
            // 빈 쿼리스트링인 경우 빈 값 반환
            if (string.IsNullOrEmpty(queryString) || queryString.Length < 1)
                return "";
            
            // '?' 제거 (예: "?modelName=test" → "modelName=test")
            string query = queryString.StartsWith("?") ? queryString.Substring(1) : queryString;
            
            // '&'로 분리된 각 쿼리 파라미터를 순회합니다.
            foreach (var pair in query.Split('&'))
            {
                var parts = pair.Split('=');
                // 파라미터 이름이 일치하면 값 반환 (URL 디코딩 포함)
                if (parts.Length >= 2 && parts[0] == paramName)
                {
                    return System.Uri.UnescapeDataString(string.Join("=", parts.Skip(1)));
                }
            }
            return "";
        }
    }

    /// <summary>
    /// 모니터링 데이터를 나타내는 데이터 구조체
    /// HTTP API를 통해 수신된 데이터를 저장합니다.
    /// </summary>
    class MonitoringData
    {
        /// <summary>요청 고유 ID</summary>
        public string RequestId { get; set; }
        
        /// <summary>데이터 생성 타임스탬프 (yyyyMMddHHmmss 형식)</summary>
        public string Timestamp { get; set; }
        
        /// <summary>에이전트 ID (AI 모델 식별자)</summary>
        public string AgentId { get; set; }
        
        /// <summary>데이터 타입 ('P': 예측 값, 'A': 실제 값)</summary>
        public string DataType { get; set; }
        
        /// <summary>데이터 값</summary>
        public string DataValue { get; set; }
    }
}