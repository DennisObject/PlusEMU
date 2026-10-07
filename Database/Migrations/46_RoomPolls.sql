CREATE TABLE IF NOT EXISTS room_polls (
    id INT NOT NULL PRIMARY KEY,
    room_id INT UNSIGNED NOT NULL UNIQUE,
    enabled BOOL NOT NULL DEFAULT TRUE,
    type VARCHAR(32) NOT NULL,
    title VARCHAR(255) NOT NULL,
    summary TEXT NOT NULL,
    end_message TEXT NOT NULL,
    nps BOOL NOT NULL DEFAULT FALSE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS room_poll_questions (
    id INT NOT NULL PRIMARY KEY,
    poll_id INT NOT NULL,
    parent_id INT NOT NULL DEFAULT 0,
    sort_order INT NOT NULL,
    type INT NOT NULL,
    text TEXT NOT NULL,
    category INT NOT NULL DEFAULT 0,
    answer_type INT NOT NULL DEFAULT 0,
    choices LONGTEXT NOT NULL,
    KEY poll_questions (poll_id, sort_order)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS room_poll_responses (
    poll_id INT NOT NULL,
    user_id INT NOT NULL,
    answers LONGTEXT NOT NULL,
    completed_at DATETIME(6) NULL,
    PRIMARY KEY (poll_id, user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
