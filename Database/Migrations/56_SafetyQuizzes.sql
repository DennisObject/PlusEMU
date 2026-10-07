-- Questions and option indices belong to the official quiz.<code> localization keys.
CREATE TABLE IF NOT EXISTS safety_quizzes (
    code VARCHAR(32) NOT NULL PRIMARY KEY,
    enabled BOOL NOT NULL DEFAULT FALSE,
    question_count INT NOT NULL,
    retry_seconds INT NOT NULL DEFAULT 7200
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS safety_quiz_questions (
    quiz_code VARCHAR(32) NOT NULL,
    question_id INT NOT NULL,
    answer_count INT NOT NULL,
    correct_answer INT NOT NULL,
    PRIMARY KEY (quiz_code, question_id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS user_safety_quizzes (
    user_id INT NOT NULL,
    quiz_code VARCHAR(32) NOT NULL,
    next_allowed_at DATETIME(6) NOT NULL,
    completed_at DATETIME(6) NULL,
    award_pending BOOL NOT NULL DEFAULT FALSE,
    PRIMARY KEY (user_id, quiz_code)
) ENGINE=InnoDB;

START TRANSACTION;
SET @seed_safety = NOT EXISTS(SELECT 1 FROM safety_quizzes WHERE BINARY code=BINARY 'SafetyQuiz1');
INSERT INTO safety_quizzes(code,enabled,question_count,retry_seconds)
SELECT 'SafetyQuiz1',TRUE,13,7200 WHERE @seed_safety;
INSERT INTO safety_quiz_questions(quiz_code,question_id,answer_count,correct_answer)
SELECT 'SafetyQuiz1',question_id,3,correct_answer FROM (
SELECT 0 AS question_id,1 AS correct_answer
UNION ALL
SELECT 1 AS question_id,1 AS correct_answer
UNION ALL
SELECT 2 AS question_id,1 AS correct_answer
UNION ALL
SELECT 3 AS question_id,1 AS correct_answer
UNION ALL
SELECT 4 AS question_id,1 AS correct_answer
UNION ALL
SELECT 5 AS question_id,0 AS correct_answer
UNION ALL
SELECT 6 AS question_id,1 AS correct_answer
UNION ALL
SELECT 7 AS question_id,1 AS correct_answer
UNION ALL
SELECT 8 AS question_id,2 AS correct_answer
UNION ALL
SELECT 9 AS question_id,2 AS correct_answer
UNION ALL
SELECT 10 AS question_id,0 AS correct_answer
UNION ALL
SELECT 11 AS question_id,0 AS correct_answer
UNION ALL
SELECT 12 AS question_id,0 AS correct_answer
) AS official_questions WHERE @seed_safety;

SET @seed_habbo_way = NOT EXISTS(SELECT 1 FROM safety_quizzes WHERE BINARY code=BINARY 'HabboWay1');
INSERT INTO safety_quizzes(code,enabled,question_count,retry_seconds)
SELECT 'HabboWay1',TRUE,5,7200 WHERE @seed_habbo_way;
INSERT INTO safety_quiz_questions(quiz_code,question_id,answer_count,correct_answer)
SELECT 'HabboWay1',question_id,4,correct_answer FROM (
SELECT 0 AS question_id,2 AS correct_answer
UNION ALL
SELECT 1 AS question_id,1 AS correct_answer
UNION ALL
SELECT 2 AS question_id,2 AS correct_answer
UNION ALL
SELECT 3 AS question_id,0 AS correct_answer
UNION ALL
SELECT 4 AS question_id,1 AS correct_answer
UNION ALL
SELECT 5 AS question_id,3 AS correct_answer
UNION ALL
SELECT 6 AS question_id,1 AS correct_answer
UNION ALL
SELECT 7 AS question_id,3 AS correct_answer
UNION ALL
SELECT 8 AS question_id,0 AS correct_answer
UNION ALL
SELECT 9 AS question_id,0 AS correct_answer
) AS official_questions WHERE @seed_habbo_way;

-- Adapt the inspected badge-only Habbo Way award through the existing achievement/talent hook.
-- Existing operator achievement groups and their economic rewards remain unchanged.
INSERT INTO achievements(group_name,category,level,reward_pixels,reward_points,progress_needed,game_id)
SELECT 'ACH_SafetyQuizGraduate','identity',1,5,5,1,0
WHERE NOT EXISTS(SELECT 1 FROM achievements WHERE BINARY group_name=BINARY 'ACH_SafetyQuizGraduate');
INSERT INTO achievements(group_name,category,level,reward_pixels,reward_points,progress_needed,game_id)
SELECT 'ACH_HabboWayGraduate','identity',1,0,0,1,0
WHERE NOT EXISTS(SELECT 1 FROM achievements WHERE BINARY group_name=BINARY 'ACH_HabboWayGraduate');
INSERT IGNORE INTO badge_definitions(code,required_right) VALUES('ACH_SafetyQuizGraduate1',''),('ACH_HabboWayGraduate1','');
COMMIT;
