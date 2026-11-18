CREATE TABLE IF NOT EXISTS tickets (
    id SERIAL PRIMARY KEY,
    title VARCHAR(120) NOT NULL,
    description TEXT NOT NULL,
    priority INT NOT NULL CHECK (priority BETWEEN 1 AND 3),
    status VARCHAR(20) NOT NULL DEFAULT 'new' CHECK (status IN ('new', 'in_progress', 'done')),
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ticket_events (
    id SERIAL PRIMARY KEY,
    ticket_id INT NOT NULL REFERENCES tickets(id) ON DELETE CASCADE,
    old_status VARCHAR(20),
    new_status VARCHAR(20) NOT NULL,
    changed_at TIMESTAMP NOT NULL DEFAULT NOW()
);

INSERT INTO tickets (title, description, priority, status) VALUES
('Не работает принтер', 'Принтер в 204 кабинете не печатает, мигает красная лампочка', 1, 'new'),
('Нет доступа к интернету', 'После обеда пропал интернет на рабочем месте', 2, 'in_progress'),
('Забыл пароль от почты', 'Не могу войти в корпоративную почту', 3, 'new'),
('Установить Excel', 'Нужен Excel на новом ноутбуке', 2, 'done'),
('Медленно работает компьютер', 'Компьютер включается около десяти минут', 1, 'in_progress'),
('Настроить сетевой диск', 'Нужен доступ к папке отдела', 3, 'done');
