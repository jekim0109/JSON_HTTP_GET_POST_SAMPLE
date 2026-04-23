using System;
using System.Collections.Generic;
using System.IO;

namespace SP_TEST
{
    /// <summary>
    /// 문제 2: 시간 범위 기반 정확도 산출
    /// 콘솔에서 입력받은 '시간 범위'에 대하여 AI 모델의 정확도를 산출하는 프로그램입니다.
    /// 문제 1의 기능을 기반으로 시간 필터링 기능이 추가되었습니다.
    /// 
    /// 시간 범위 형식: yyyyMMddHH (예: 2025041010 → 20250410100000 ~ 20250410105959)
    /// 필터링 기준: 'P'(예측 값) 데이터의 Timestamp를 기준으로 시간 범위 적용
    /// </summary>
    class Program
    {
        /// <summary>
        /// 프로그램 진입점
        /// 콘솔에서 시간 범위를 입력 받아 해당 범위의 AI 모델 정확도를 계산하고 출력합니다.
        /// </summary>
        /// <param name="args">명령줄 인수 (사용되지 않음)</param>
        static void Main(string[] args)
        {
            // 콘솔에서 시간 범위 입력 받음 (형식: yyyyMMddHH)
            // 예: "2025041010" → 2025년 4월 10일 10시 해당 시간대의 데이터를 의미
            string timeWindow = Console.ReadLine()?.Trim();
            
            // 입력값이 비어있거나 길이가 10 미만이면(yyyyMMddHH 형식이 아니면) 종료
            if (string.IsNullOrEmpty(timeWindow) || timeWindow.Length < 10)
            {
                return;
            }
            
            // 시간 범위 파싱: yyyyMMddHH 형식을 연, 월, 일, 시로 분리
            // 예: "2025041010" → year=2025, month=04, day=10, hour=10
            int year = int.Parse(timeWindow.Substring(0, 4));   // 연도 (4자리)
            int month = int.Parse(timeWindow.Substring(4, 2));  // 월 (2자리)
            int day = int.Parse(timeWindow.Substring(6, 2));    // 일 (2자리)
            int hour = int.Parse(timeWindow.Substring(8, 2));   // 시 (2자리)
            
            // 시간 범위의 시작과 끝 Timestamp를 생성합니다.
            // startTimestamp: 해당 시간대의 시작시점 (00분 00초 00밀리초)
            // endTimestamp: 해당 시간대의 끝시점 (59분 59초 59밀리초)
            // 예: "2025041010" → start="20250410100000", end="20250410105959"
            string startTimestamp = $"{year:0000}{month:00}{day:00}{hour:00}0000";
            string endTimestamp = $"{year:0000}{month:00}{day:00}{hour:00}5959";
            
            // MONITORING.TXT 파일 경로 설정 (프로그램 실행 위치 기준 상대경로)
            string monitoringFile = Path.Combine(Directory.GetCurrentDirectory(), "MONITORING.TXT");
            
            // 파일 읽기 - P(예측) 데이터의 Timestamp를 기준으로 시간 범위 제한
            // 구조: Dictionary<요청ID, Dictionary<데이터필드('P'/'A'/'P_timestamp'), 값>>
            var dataMap = new Dictionary<string, Dictionary<string, string>>();
            
            // MONITORING.TXT 파일이 존재하는지 확인하고, 존재하면 파일 읽기
            if (File.Exists(monitoringFile))
            {
                // 파일의 모든 줄을 한 줄씩 읽어옵니다.
                foreach (var line in File.ReadAllLines(monitoringFile))
                {
                    // 빈 줄이나 공백만 있는 줄은 건너뜁니다.
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    
                    // '#' 구분자로 파일을 파싱하여 4개의 필드를 추출합니다.
                    var parts = line.Split('#');
                    // 형식이 맞지 않는 줄은 건너뜁니다.
                    if (parts.Length != 4)
                        continue;
                    
                    string requestId = parts[0];    // 요청ID (예: req001)
                    string timestamp = parts[1];    // Timestamp (yyyyMMddHHmmss 형태)
                    string dataType = parts[2];     // 데이터 타입 ('P' 또는 'A')
                    string dataValue = parts[3];    // 데이터 값
                    
                    // 해당 요청ID가 딕셔너리에 없으면 새로 생성합니다.
                    if (!dataMap.ContainsKey(requestId))
                    {
                        dataMap[requestId] = new Dictionary<string, string>();
                    }
                    
                    // 데이터 타입('P' 또는 'A')을 키로 하여 데이터 값을 저장합니다.
                    dataMap[requestId][dataType] = dataValue;
                    
                    // P(예측 값) 데이터인 경우, 해당 데이터의 Timestamp를 별도로 저장합니다.
                    // 이 Timestamp를 기준으로 시간 범위 필터링을 수행합니다.
                    if (dataType == "P")
                    {
                        dataMap[requestId]["P_timestamp"] = timestamp;
                    }
                }
            }
            
            // 시간 범위 내 데이터만 필터링하여 정확도 계산
            int total = 0;    // 시간 범위 내 모니터링 요청 수
            int correct = 0;  // 시간 범위 내 예측값과 실제값이 일치한 요청 수
            
            // 저장된 모든 요청ID에 대해 반복합니다.
            foreach (var kvp in dataMap)
            {
                // 해당 요청의 P(예측) 데이터 Timestamp를 가져옵니다.
                string pTimestamp = kvp.Value.ContainsKey("P_timestamp") ? kvp.Value["P_timestamp"] : "";
                
                // P 데이터의 Timestamp가 시간 범위 내에 있는지 확인합니다.
                // Timestamp가 없거나, 시작시간보다 이전이거나, 끝시간보다 이후면 해당 요청을 건너뜁니다.
                if (string.IsNullOrEmpty(pTimestamp) || int.Parse(pTimestamp) < int.Parse(startTimestamp) || int.Parse(pTimestamp) > int.Parse(endTimestamp))
                    continue;
                
                // 시간 범위 내의 데이터만 처리: 예측값(P)과 실제값(A)을 가져옵니다.
                string pValue = kvp.Value.ContainsKey("P") ? kvp.Value["P"] : "";
                string aValue = kvp.Value.ContainsKey("A") ? kvp.Value["A"] : "";
                
                total++;
                if (pValue == aValue)
                {
                    correct++;
                }
            }
            
            // 결과 출력
            Console.WriteLine($"{correct}/{total}");
        }
    }
}