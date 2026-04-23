using System;
using System.Collections.Generic;
using System.IO;

namespace SP_TEST
{
    /// <summary>
    /// 문제 1: 모니터링 데이터 기반 정확도 산출
    /// AI 모델의 예측 값과 실제 값이 수집된 MONITORING.TXT 파일을 읽고,
    /// AI 모델의 정확도를 산출하여 콘솔에 출력하는 프로그램입니다.
    /// 
    /// 파일 형식: <요청ID>#<Timestamp>#<데이터 타입>#<데이터 값>
    /// - 요청ID: 모니터링된 요청의 고유 구분자 (예: req001)
    /// - Timestamp: yyyyMMddHHmmss 형태 (예: 20250410100000)
    /// - 데이터 타입: 'P'(예측 값), 'A'(실제 값)
    /// - 데이터 값: P인 경우 AI 예측값, A인 경우 실제 검증값
    /// </summary>
    class Program
    {
        /// <summary>
        /// 프로그램 진입점
        /// MONITORING.TXT 파일을 읽어 AI 모델의 정확도를 계산하고 출력합니다.
        /// </summary>
        /// <param name="args">명령줄 인수 (사용되지 않음)</param>
        static void Main(string[] args)
        {
            // MONITORING.TXT 파일 경로 설정
            // 프로그램이 실행되는 현재 디렉토리를 기준으로 상대경로로 파일 위치 지정
            string monitoringFile = Path.Combine(Directory.GetCurrentDirectory(), "MONITORING.TXT");
            
            // 요청ID를 키로 하고, 값으로 P(예측)와 A(실제) 데이터를 저장하는 딕셔너리
            // 구조: Dictionary<요청ID, Dictionary<데이터타입('P'/'A'), 데이터값>>
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
                    // 형식이 맞지 않는 줄은 건너뜁니다. (요청ID, Timestamp, 데이터타입, 데이터값 필요)
                    if (parts.Length != 4)
                        continue;
                    
                    // 각 필드 값을 변수에 할당합니다.
                    string requestId = parts[0];    // 요청ID (예: req001)
                    string timestamp = parts[1];    // Timestamp (yyyyMMddHHmmss 형태)
                    string dataType = parts[2];     // 데이터 타입 ('P' 또는 'A')
                    string dataValue = parts[3];    // 데이터 값 (AI 예측값 또는 실제 검증값)
                    
                    // 해당 요청ID가 딕셔너리에 없으면 새로 생성합니다.
                    if (!dataMap.ContainsKey(requestId))
                    {
                        dataMap[requestId] = new Dictionary<string, string>();
                    }
                    
                    // 데이터 타입('P' 또는 'A')을 키로 하여 데이터 값을 저장합니다.
                    // 동일한 요청ID에 대해 P와 A 데이터가 각각 저장됩니다.
                    dataMap[requestId][dataType] = dataValue;
                }
            }
            
            // 정확도 계산: 전체 요청 수와 예측값과 실제값이 일치하는 요청 수
            // dataMap의 키(요청ID) 개수가 전체 모니터링 대상 요청 수를 의미합니다.
            int total = dataMap.Count;
            int correct = 0;
            
            // 각 요청ID별로 예측값(P)과 실제값(A)를 비교합니다.
            foreach (var kvp in dataMap)
            {
                // 해당 요청의 예측값(P)을 가져옵니다. 없으면 빈 문자열.
                string pValue = kvp.Value.ContainsKey("P") ? kvp.Value["P"] : "";
                // 해당 요청의 실제값(A)을 가져옵니다. 없으면 빈 문자열.
                string aValue = kvp.Value.ContainsKey("A") ? kvp.Value["A"] : "";
                
                // 예측값과 실제값이 일치하면 correct 카운트를 증가시킵니다.
                if (pValue == aValue)
                {
                    correct++;
                }
            }
            
            // 정확도 결과를 콘솔에 출력합니다.
            // 형식: "<일치한 요청 수>/<전체 모니터링 요청 수>" (예: "8/10")
            Console.WriteLine($"{correct}/{total}");
        }
    }
}
