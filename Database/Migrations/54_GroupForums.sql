CREATE TABLE IF NOT EXISTS group_forums (
    group_id INT UNSIGNED NOT NULL PRIMARY KEY,
    read_permission TINYINT UNSIGNED NOT NULL DEFAULT 0,
    post_permission TINYINT UNSIGNED NOT NULL DEFAULT 0,
    thread_permission TINYINT UNSIGNED NOT NULL DEFAULT 0,
    moderate_permission TINYINT UNSIGNED NOT NULL DEFAULT 2,
    message_count INT NOT NULL DEFAULT 0,
    FOREIGN KEY (group_id) REFERENCES `groups` (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS group_forum_threads (
    id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    group_id INT UNSIGNED NOT NULL,
    author_id INT NOT NULL,
    title VARCHAR(120) NOT NULL,
    pinned BOOL NOT NULL DEFAULT FALSE,
    locked BOOL NOT NULL DEFAULT FALSE,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    message_count INT NOT NULL DEFAULT 0,
    last_message_id INT NOT NULL DEFAULT 0,
    state TINYINT UNSIGNED NOT NULL DEFAULT 0,
    moderator_id INT NOT NULL DEFAULT 0,
    moderated_at DATETIME(6) NULL,
    KEY forum_threads (group_id, pinned, updated_at),
    UNIQUE KEY forum_thread_identity (group_id, id),
    FOREIGN KEY (group_id) REFERENCES group_forums (group_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS group_forum_messages (
    group_id INT UNSIGNED NOT NULL,
    id INT NOT NULL,
    thread_id INT NOT NULL,
    message_index INT NOT NULL,
    author_id INT NOT NULL,
    body TEXT NOT NULL,
    created_at DATETIME(6) NOT NULL,
    state TINYINT UNSIGNED NOT NULL DEFAULT 0,
    moderator_id INT NOT NULL DEFAULT 0,
    moderated_at DATETIME(6) NULL,
    PRIMARY KEY (group_id, id),
    UNIQUE KEY thread_messages (thread_id, message_index),
    KEY author_messages (author_id),
    FOREIGN KEY (group_id, thread_id) REFERENCES group_forum_threads (group_id, id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS group_forum_read_markers (
    group_id INT UNSIGNED NOT NULL,
    user_id INT NOT NULL,
    last_message_id INT NOT NULL DEFAULT 0,
    PRIMARY KEY (group_id, user_id),
    FOREIGN KEY (group_id) REFERENCES group_forums (group_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS group_forum_post_limits (
    user_id INT NOT NULL PRIMARY KEY,
    posted_at DATETIME(6) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
