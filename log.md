rag-ollama       | slot launch_slot_: id  0 | task -1 | sampler params:
rag-ollama       |      repeat_last_n = 64, repeat_penalty = 1.000, frequency_penalty = 0.000, presence_penalty = 0.000
rag-ollama       |      dry_multiplier = 0.000, dry_base = 1.750, dry_allowed_length = 2, dry_penalty_last_n = 64
rag-ollama       |      top_k = 20, top_p = 1.000, min_p = 0.000, xtc_probability = 0.000, xtc_threshold = 0.100, typical_p = 1.000, top_n_sigma = -1.000, temp = 0.300
rag-ollama       |      mirostat = 0, mirostat_lr = 0.100, mirostat_ent = 5.000, adaptive_target = -1.000, adaptive_decay = 0.900
rag-ollama       | slot launch_slot_: id  0 | task 1314 | processing task, is_child = 0
rag-ollama       | slot   operator(): id  0 | task 1314 | new prompt, n_ctx_slot = 4096, n_keep = 4, task.n_tokens = 321
rag-ollama       | slot   operator(): id  0 | task 1314 | cached n_tokens = 71, memory_seq_rm [71, end)
rag-ollama       | slot init_sampler: id  0 | task 1314 | init sampler, took 0.06 ms, tokens: text = 321, total = 321
rag-backend      | info: System.Net.Http.HttpClient.IRagService.LogicalHandler[100]
rag-backend      |       Start processing HTTP request GET http://rag:8000/health
rag-backend      | info: System.Net.Http.HttpClient.IRagService.ClientHandler[100]
rag-backend      |       Sending HTTP request GET http://rag:8000/health
rag-rag-service  | INFO:     172.16.1.5:56900 - "GET /health HTTP/1.1" 200 OK
rag-backend      | info: System.Net.Http.HttpClient.IRagService.ClientHandler[101]
rag-backend      |       Received HTTP response headers after 7.4787ms - 200
rag-backend      | info: System.Net.Http.HttpClient.IRagService.LogicalHandler[101]
rag-backend      |       End processing HTTP request after 7.5564ms - 200
rag-rag-service  | INFO:     127.0.0.1:56954 - "GET /health HTTP/1.1" 200 OK
rag-ollama       | slot print_timing: id  0 | task 1314 | prompt eval time =    7461.79 ms /   250 tokens (   29.85 ms per token,    33.50 tokens per second)
rag-ollama       | slot print_timing: id  0 | task 1314 |        eval time =    1765.09 ms /    22 tokens (   84.05 ms per token,    11.90 tokens per second)
rag-ollama       | slot print_timing: id  0 | task 1314 |       total time =    9226.88 ms /   272 tokens
rag-ollama       | slot print_timing: id  0 | task 1314 |    graphs reused =       1290
rag-ollama       | slot      release: id  0 | task 1314 | stop processing: n_tokens = 342, truncated = 0
rag-ollama       | srv  update_slots: all slots are idle
rag-ollama       | [GIN] 2026/09/19 - 16:55:59 | 200 | 37.708408376s |      172.16.1.4 | POST     "/v1/chat/completions"
rag-rag-service  | 2026-09-19 16:55:59,844 INFO httpx: HTTP Request: POST http://ollama:11434/v1/chat/completions "HTTP/1.1 200 OK"
rag-ollama       | slot get_availabl: id  0 | task -1 |  - checking sim = 0.167 (1/6) > 0.100
rag-ollama       | slot get_availabl: id  0 | task -1 | selected slot by LCP similarity, f_sim_best = 0.167 (> 0.100 thold), f_keep = 0.083
rag-ollama       | slot launch_slot_: id  0 | task 118 | processing task, is_child = 0
rag-ollama       | slot   operator(): id  0 | task 118 | new prompt, n_ctx_slot = 4096, n_keep = 0, task.n_tokens = 6
rag-ollama       | slot   operator(): id  0 | task 118 | cached n_tokens = 0, memory_seq_rm [0, end)
rag-ollama       | slot      release: id  0 | task 118 | stop processing: n_tokens = 6, truncated = 0
rag-ollama       | srv  update_slots: all slots are idle
rag-ollama       | slot get_availabl: id  0 | task -1 |  - checking sim = 0.111 (1/9) > 0.100
rag-ollama       | slot get_availabl: id  0 | task -1 | selected slot by LCP similarity, f_sim_best = 0.111 (> 0.100 thold), f_keep = 0.167
rag-ollama       | slot launch_slot_: id  0 | task 120 | processing task, is_child = 0
rag-ollama       | slot   operator(): id  0 | task 120 | new prompt, n_ctx_slot = 4096, n_keep = 0, task.n_tokens = 9
rag-ollama       | slot   operator(): id  0 | task 120 | cached n_tokens = 0, memory_seq_rm [0, end)
rag-ollama       | slot      release: id  0 | task 120 | stop processing: n_tokens = 9, truncated = 0
rag-ollama       | srv  update_slots: all slots are idle
rag-ollama       | slot get_availabl: id  0 | task -1 |  - checking sim = 0.167 (1/6) > 0.100
rag-ollama       | slot get_availabl: id  0 | task -1 | selected slot by LCP similarity, f_sim_best = 0.167 (> 0.100 thold), f_keep = 0.111
rag-ollama       | slot launch_slot_: id  0 | task 122 | processing task, is_child = 0
rag-ollama       | slot   operator(): id  0 | task 122 | new prompt, n_ctx_slot = 4096, n_keep = 0, task.n_tokens = 6
rag-ollama       | slot   operator(): id  0 | task 122 | cached n_tokens = 0, memory_seq_rm [0, end)
rag-ollama       | slot      release: id  0 | task 122 | stop processing: n_tokens = 6, truncated = 0
rag-ollama       | srv  update_slots: all slots are idle
rag-rag-service  | 2026-09-19 16:56:00,692 INFO httpx: HTTP Request: POST http://ollama:11434/v1/embeddings "HTTP/1.1 200 OK"
rag-ollama       | [GIN] 2026/09/19 - 16:56:00 | 200 |  844.232336ms |      172.16.1.4 | POST     "/v1/embeddings"
rag-rag-service  | 2026-09-19 16:56:00,882 INFO httpx2: HTTP Request: POST https://api.groq.com/openai/v1/chat/completions "HTTP/1.1 429 Too Many Requests"
rag-rag-service  | 2026-09-19 16:56:00,885 WARNING app.pipeline.agentic: Planificación agéntica falló (Error code: 429 - {'error': {'message': 'Rate limit reached for model `openai/gpt-oss-20b` in organization `org_01jr5vf1c0ft3rbqy6pjckrmkb` service tier `on_demand` on tokens per day (TPD): Limit 200000, Used 199911, Requested 2831. Please try again in 19m44.544s. Need more tokens? Upgrade to Dev Tier today at https://console.groq.com/settings/billing', 'type': 'tokens', 'code': 'rate_limit_exceeded'}}); fallback a pipeline fijo
rag-rag-service  | 2026-09-19 16:56:00,924 INFO httpx: HTTP Request: POST https://api.groq.com/openai/v1/chat/completions "HTTP/2 429 Too Many Requests"
rag-rag-service  | 2026-09-19 16:56:00,926 WARNING app.core.llms: Fallo groq:openai/gpt-oss-20b (HTTPStatusError); probando siguiente eslabón
rag-ollama       | srv  server_strea: conv_id= (empty=1)
rag-ollama       | slot get_availabl: id  0 | task -1 |  - checking sim = 0.733 (55/75) > 0.100
rag-ollama       | slot get_availabl: id  0 | task -1 | selected slot by LCP similarity, f_sim_best = 0.733 (> 0.100 thold), f_keep = 0.161
rag-ollama       | srv  get_availabl: updating prompt cache
rag-ollama       | srv   prompt_save:  - saving prompt with length 342, total state size = 48.099 MiB (draft: 0.000 MiB)
rag-ollama       | srv          load:  - looking for better prompt, base f_keep = 0.161, f_sim = 0.733
rag-ollama       | srv          load:    - prompt with length    2051, lcp =       3, f_keep = 0.001, f_sim = 0.040
rag-ollama       | srv          load:    - prompt with length    1392, lcp =       3, f_keep = 0.002, f_sim = 0.040
rag-ollama       | srv          load:    - prompt with length    1280, lcp =       3, f_keep = 0.002, f_sim = 0.040
rag-ollama       | srv          load:    - prompt with length    2051, lcp =       3, f_keep = 0.001, f_sim = 0.040
rag-ollama       | srv          load:    - prompt with length     374, lcp =      75, f_keep = 0.201, f_sim = 1.000
rag-ollama       | srv          load:    - prompt with length    2051, lcp =       3, f_keep = 0.001, f_sim = 0.040
rag-ollama       | srv          load:    - prompt with length     323, lcp =      55, f_keep = 0.170, f_sim = 0.733
rag-ollama       | srv          load:    - prompt with length     342, lcp =      55, f_keep = 0.161, f_sim = 0.733
rag-ollama       | srv        update:  - cache state: 8 prompts, 1387.245 MiB (limits: 8192.000 MiB, 4096 tokens, 58249 est)
rag-ollama       | srv        update:    - prompt 0x1fdc7480:    2051 tokens, checkpoints:  0,   288.446 MiB
rag-ollama       | srv        update:    - prompt 0x1fdcf940:    1392 tokens, checkpoints:  0,   195.767 MiB
rag-ollama       | srv        update:    - prompt 0x1fdcc530:    1280 tokens, checkpoints:  0,   180.015 MiB
rag-ollama       | srv        update:    - prompt 0x1fdc6a90:    2051 tokens, checkpoints:  0,   288.446 MiB
rag-ollama       | srv        update:    - prompt 0x1fdc5da0:     374 tokens, checkpoints:  0,    52.599 MiB
rag-ollama       | srv        update:    - prompt 0x1fdc3fc0:    2051 tokens, checkpoints:  0,   288.446 MiB
rag-ollama       | srv        update:    - prompt 0x230b70f0:     323 tokens, checkpoints:  0,    45.426 MiB
rag-ollama       | srv        update:    - prompt 0x1fe65df0:     342 tokens, checkpoints:  0,    48.099 MiB
rag-ollama       | srv  get_availabl: prompt cache update took 53.52 ms
rag-ollama       | slot launch_slot_: id  0 | task -1 | sampler chain: logits -> ?penalties -> ?dry -> ?top-n-sigma -> top-k -> ?typical -> ?top-p -> ?min-p -> ?xtc -> temp-ext -> dist
rag-ollama       | slot launch_slot_: id  0 | task -1 | sampler params:
rag-ollama       |      repeat_last_n = 64, repeat_penalty = 1.000, frequency_penalty = 0.000, presence_penalty = 0.000
rag-ollama       |      dry_multiplier = 0.000, dry_base = 1.750, dry_allowed_length = 2, dry_penalty_last_n = 64
rag-ollama       |      top_k = 20, top_p = 1.000, min_p = 0.000, xtc_probability = 0.000, xtc_threshold = 0.100, typical_p = 1.000, top_n_sigma = -1.000, temp = 0.300
rag-ollama       |      mirostat = 0, mirostat_lr = 0.100, mirostat_ent = 5.000, adaptive_target = -1.000, adaptive_decay = 0.900
rag-ollama       | slot launch_slot_: id  0 | task 1338 | processing task, is_child = 0
rag-ollama       | slot   operator(): id  0 | task 1338 | new prompt, n_ctx_slot = 4096, n_keep = 4, task.n_tokens = 75
rag-ollama       | slot   operator(): id  0 | task 1338 | cached n_tokens = 55, memory_seq_rm [55, end)
rag-ollama       | slot init_sampler: id  0 | task 1338 | init sampler, took 0.02 ms, tokens: text = 75, total = 75
rag-rag-service  | INFO:     127.0.0.1:35526 - "GET /health HTTP/1.1" 200 OK
rag-ollama       | slot print_timing: id  0 | task 1338 | n_gen =    100, tg =   8.30 t/s, tg_3s =   8.38 t/s
rag-ollama       | slot print_timing: id  0 | task 1338 | n_gen =    139, tg =   9.22 t/s, tg_3s =  12.80 t/s
rag-ollama       | slot print_timing: id  0 | task 1338 | n_gen =    175, tg =   9.63 t/s, tg_3s =  11.65 t/s
rag-ollama       | slot print_timing: id  0 | task 1338 | n_gen =    203, tg =   9.55 t/s, tg_3s =   9.05 t/s
rag-backend      | info: System.Net.Http.HttpClient.IRagService.LogicalHandler[100]
rag-backend      |       Start processing HTTP request GET http://rag:8000/health
rag-backend      | info: System.Net.Http.HttpClient.IRagService.ClientHandler[100]
rag-backend      |       Sending HTTP request GET http://rag:8000/health
rag-rag-service  | INFO:     172.16.1.5:54044 - "GET /health HTTP/1.1" 200 OK
rag-backend      | info: System.Net.Http.HttpClient.IRagService.ClientHandler[101]
rag-backend      |       Received HTTP response headers after 7.2763ms - 200
rag-backend      | info: System.Net.Http.HttpClient.IRagService.LogicalHandler[101]
rag-backend      |       End processing HTTP request after 7.3638ms - 200
rag-rag-service  | INFO:     127.0.0.1:53558 - "GET /health HTTP/1.1" 200 OK
rag-ollama       | slot print_timing: id  0 | task 1338 | n_gen =    223, tg =   9.13 t/s, tg_3s =   6.34 t/s
rag-ollama       | slot print_timing: id  0 | task 1338 | n_gen =    246, tg =   8.95 t/s, tg_3s =   7.54 t/s
rag-ollama       | slot print_timing: id  0 | task 1338 | n_gen =    268, tg =   8.78 t/s, tg_3s =   7.25 t/s
rag-ollama       | slot print_timing: id  0 | task 1338 | n_gen =    291, tg =   8.64 t/s, tg_3s =   7.23 t/s
rag-ollama       | slot print_timing: id  0 | task 1338 | prompt eval time =    2304.98 ms /    20 tokens (  115.25 ms per token,     8.68 tokens per second)
rag-ollama       | slot print_timing: id  0 | task 1338 |        eval time =   34973.51 ms /   300 tokens (  116.97 ms per token,     8.55 tokens per second)
rag-ollama       | slot print_timing: id  0 | task 1338 |       total time =   37278.49 ms /   320 tokens
rag-ollama       | slot print_timing: id  0 | task 1338 |    graphs reused =       1587
rag-ollama       | slot      release: id  0 | task 1338 | stop processing: n_tokens = 374, truncated = 0
rag-ollama       | srv  update_slots: all slots are idle
rag-ollama       | [GIN] 2026/09/19 - 16:56:38 | 200 |  37.34666911s |      172.16.1.4 | POST     "/v1/chat/completions"
rag-rag-service  | 2026-09-19 16:56:38,275 INFO httpx: HTTP Request: POST http://ollama:11434/v1/chat/completions "HTTP/1.1 200 OK"
rag-rag-service  | 2026-09-19 16:56:38,277 WARNING app.core.llms: Fallo ollama:qwen3:8b (RuntimeError); probando siguiente eslabón
rag-rag-service  | 2026-09-19 16:56:38,278 WARNING app.retrieval.engine: Expansión falló (El proveedor de IA no está disponible (chat). Inténtalo de nuevo más tarde.); single-query
rag-rag-service  | 2026-09-19 16:56:38,366 INFO httpx: HTTP Request: POST https://api.groq.com/openai/v1/chat/completions "HTTP/2 429 Too Many Requests"
rag-rag-service  | 2026-09-19 16:56:38,369 WARNING app.core.llms: Fallo groq:openai/gpt-oss-20b (HTTPStatusError); probando siguiente eslabón
rag-ollama       | srv  server_strea: conv_id= (empty=1)
rag-ollama       | slot get_availabl: id  0 | task -1 |  - checking sim = 0.001 (3/3237) > 0.100
rag-ollama       | slot get_availabl: id  0 | task -1 | selected slot by LRU, t_last = 3469798955408
rag-ollama       | srv  get_availabl: updating prompt cache
rag-ollama       | srv   prompt_save:  - saving prompt with length 374, total state size = 52.599 MiB (draft: 0.000 MiB)
rag-ollama       | srv          load:  - looking for better prompt, base f_keep = 0.008, f_sim = 0.001
rag-ollama       | srv          load:    - prompt with length    2051, lcp =      89, f_keep = 0.043, f_sim = 0.027
rag-ollama       | srv          load:    - prompt with length    1392, lcp =       3, f_keep = 0.002, f_sim = 0.001
rag-ollama       | srv          load:    - prompt with length    1280, lcp =       3, f_keep = 0.002, f_sim = 0.001
rag-ollama       | srv          load:    - prompt with length    2051, lcp =      98, f_keep = 0.048, f_sim = 0.030
rag-ollama       | srv          load:    - prompt with length     374, lcp =       3, f_keep = 0.008, f_sim = 0.001
rag-ollama       | srv          load:    - prompt with length    2051, lcp =    2051, f_keep = 1.000, f_sim = 0.634
rag-ollama       | srv          load:    - prompt with length     323, lcp =       3, f_keep = 0.009, f_sim = 0.001
rag-ollama       | srv          load:    - prompt with length     342, lcp =       3, f_keep = 0.009, f_sim = 0.001
rag-ollama       | srv          load:    - prompt with length     374, lcp =       3, f_keep = 0.008, f_sim = 0.001
rag-ollama       | srv          load:  - found better prompt with f_keep = 1.000, f_sim = 0.634
rag-ollama       | srv        update:  - cache state: 8 prompts, 1151.397 MiB (limits: 8192.000 MiB, 4096 tokens, 58249 est)
rag-ollama       | srv        update:    - prompt 0x1fdc7480:    2051 tokens, checkpoints:  0,   288.446 MiB
rag-ollama       | srv        update:    - prompt 0x1fdcf940:    1392 tokens, checkpoints:  0,   195.767 MiB
rag-ollama       | srv        update:    - prompt 0x1fdcc530:    1280 tokens, checkpoints:  0,   180.015 MiB
rag-ollama       | srv        update:    - prompt 0x1fdc6a90:    2051 tokens, checkpoints:  0,   288.446 MiB
rag-ollama       | srv        update:    - prompt 0x1fdc5da0:     374 tokens, checkpoints:  0,    52.599 MiB
rag-ollama       | srv        update:    - prompt 0x230b70f0:     323 tokens, checkpoints:  0,    45.426 MiB
rag-ollama       | srv        update:    - prompt 0x1fe65df0:     342 tokens, checkpoints:  0,    48.099 MiB
rag-ollama       | srv        update:    - prompt 0x1fe4c710:     374 tokens, checkpoints:  0,    52.599 MiB
rag-ollama       | srv  get_availabl: prompt cache update took 133.90 ms
rag-ollama       | slot launch_slot_: id  0 | task -1 | sampler chain: logits -> ?penalties -> ?dry -> ?top-n-sigma -> top-k -> ?typical -> ?top-p -> ?min-p -> ?xtc -> temp-ext -> dist
rag-ollama       | slot launch_slot_: id  0 | task -1 | sampler params:
rag-ollama       |      repeat_last_n = 64, repeat_penalty = 1.000, frequency_penalty = 0.000, presence_penalty = 0.000
rag-ollama       |      dry_multiplier = 0.000, dry_base = 1.750, dry_allowed_length = 2, dry_penalty_last_n = 64
rag-ollama       |      top_k = 20, top_p = 1.000, min_p = 0.000, xtc_probability = 0.000, xtc_threshold = 0.100, typical_p = 1.000, top_n_sigma = -1.000, temp = 0.000
rag-ollama       |      mirostat = 0, mirostat_lr = 0.100, mirostat_ent = 5.000, adaptive_target = -1.000, adaptive_decay = 0.900
rag-ollama       | slot launch_slot_: id  0 | task 1639 | processing task, is_child = 0
rag-ollama       | slot   operator(): id  0 | task 1639 | new prompt, n_ctx_slot = 4096, n_keep = 4, task.n_tokens = 3237
rag-ollama       | slot   operator(): id  0 | task 1639 | cached n_tokens = 2051, memory_seq_rm [2051, end)
rag-rag-service  | INFO:     127.0.0.1:55364 - "GET /health HTTP/1.1" 200 OK
rag-backend      | info: System.Net.Http.HttpClient.IRagService.LogicalHandler[100]
rag-backend      |       Start processing HTTP request GET http://rag:8000/health
rag-backend      | info: System.Net.Http.HttpClient.IRagService.ClientHandler[100]
rag-backend      |       Sending HTTP request GET http://rag:8000/health
rag-rag-service  | INFO:     172.16.1.5:34302 - "GET /health HTTP/1.1" 200 OK
rag-backend      | info: System.Net.Http.HttpClient.IRagService.ClientHandler[101]
rag-backend      |       Received HTTP response headers after 5.0479ms - 200
rag-backend      | info: System.Net.Http.HttpClient.IRagService.LogicalHandler[101]
rag-backend      |       End processing HTTP request after 5.288ms - 200
rag-ollama       | slot print_timing: id  0 | task 1639 | prompt processing, n_tokens =    512, progress = 0.79, t =  17.64 s / 29.02 tokens per second
rag-ollama       | slot   operator(): id  0 | task 1639 | cached n_tokens = 2563, memory_seq_rm [2563, end)
rag-rag-service  | INFO:     127.0.0.1:43200 - "GET /health HTTP/1.1" 200 OK
rag-rag-service  | INFO:     127.0.0.1:52178 - "GET /health HTTP/1.1" 200 OK
rag-ollama       | slot print_timing: id  0 | task 1639 | prompt processing, n_tokens =   1024, progress = 0.95, t =  38.75 s / 26.43 tokens per second
rag-ollama       | slot   operator(): id  0 | task 1639 | cached n_tokens = 3075, memory_seq_rm [3075, end)
rag-ollama       | slot init_sampler: id  0 | task 1639 | init sampler, took 4.62 ms, tokens: text = 3237, total = 3237
rag-backend      | info: System.Net.Http.HttpClient.IRagService.LogicalHandler[100]
rag-backend      |       Start processing HTTP request GET http://rag:8000/health
rag-backend      | info: System.Net.Http.HttpClient.IRagService.ClientHandler[100]
rag-backend      |       Sending HTTP request GET http://rag:8000/health
rag-rag-service  | INFO:     172.16.1.5:52710 - "GET /health HTTP/1.1" 200 OK
rag-backend      | info: System.Net.Http.HttpClient.IRagService.ClientHandler[101]
rag-backend      |       Received HTTP response headers after 3.8104ms - 200
rag-backend      | info: System.Net.Http.HttpClient.IRagService.LogicalHandler[101]
rag-backend      |       End processing HTTP request after 4.2579ms - 200
rag-rag-service  | INFO:     127.0.0.1:50226 - "GET /health HTTP/1.1" 200 OK
rag-rag-service  | 2026-09-19 16:57:38,372 WARNING app.core.llms: Fallo ollama:qwen3:8b (ReadTimeout); probando siguiente eslabón
rag-rag-service  | 2026-09-19 16:57:38,372 WARNING app.retrieval.engine: Rerank falló (El proveedor de IA no está disponible (chat). Inténtalo de nuevo más tarde.); fail-open
rag-ollama       | [GIN] 2026/09/19 - 16:57:38 | 500 |          1m0s |      172.16.1.4 | POST     "/v1/chat/completions"
rag-ollama       | srv          stop: cancel task, id_task = 1639
rag-ollama       | srv  server_strea: conv_id= (empty=1)
rag-ollama       | slot get_availabl: id  0 | task -1 |  - checking sim = 1.000 (1635/1635) > 0.100
rag-ollama       | slot get_availabl: id  0 | task -1 | selected slot by LCP similarity, f_sim_best = 1.000 (> 0.100 thold), f_keep = 0.820
rag-ollama       | slot launch_slot_: id  0 | task -1 | sampler chain: logits -> ?penalties -> ?dry -> ?top-n-sigma -> top-k -> ?typical -> ?top-p -> ?min-p -> ?xtc -> temp-ext -> dist
rag-ollama       | slot launch_slot_: id  0 | task -1 | sampler params:
rag-ollama       |      repeat_last_n = 64, repeat_penalty = 1.000, frequency_penalty = 0.000, presence_penalty = 0.000
rag-ollama       |      dry_multiplier = 0.000, dry_base = 1.750, dry_allowed_length = 2, dry_penalty_last_n = 64
rag-ollama       |      top_k = 40, top_p = 1.000, min_p = 0.000, xtc_probability = 0.000, xtc_threshold = 0.100, typical_p = 1.000, top_n_sigma = -1.000, temp = 0.200
rag-ollama       |      mirostat = 0, mirostat_lr = 0.100, mirostat_ent = 5.000, adaptive_target = -1.000, adaptive_decay = 0.900
rag-ollama       | slot launch_slot_: id  0 | task 1723 | processing task, is_child = 0
rag-ollama       | slot   operator(): id  0 | task 1723 | new prompt, n_ctx_slot = 4096, n_keep = 4, task.n_tokens = 1635
rag-ollama       | slot   operator(): id  0 | task 1723 | need to evaluate at least 1 token for each active slot (n_past = 1635, task.n_tokens() = 1635)
rag-ollama       | slot   operator(): id  0 | task 1723 | n_past was set to 1634
rag-ollama       | slot   operator(): id  0 | task 1723 | cached n_tokens = 1634, memory_seq_rm [1634, end)
rag-ollama       | slot init_sampler: id  0 | task 1723 | init sampler, took 0.29 ms, tokens: text = 1635, total = 1635
rag-rag-service  | 2026-09-19 16:57:39,335 INFO httpx: HTTP Request: POST http://ollama:11434/v1/chat/completions "HTTP/1.1 200 OK"
rag-ollama       | slot      release: id  0 | task 1639 | stop processing: n_tokens = 3312, truncated = 0
rag-ollama       | srv  update_slots: all slots are idle
rag-rag-service  | INFO:     127.0.0.1:34826 - "GET /health HTTP/1.1" 200 OK
rag-ollama       | slot print_timing: id  0 | task 1723 | n_gen =    100, tg =  13.84 t/s, tg_3s =  13.97 t/s
rag-ollama       | slot print_timing: id  0 | task 1723 | n_gen =    182, tg =  17.79 t/s, tg_3s =  27.18 t/s
rag-ollama       | slot print_timing: id  0 | task 1723 | n_gen =    243, tg =  18.37 t/s, tg_3s =  20.31 t/s
rag-ollama       | slot print_timing: id  0 | task 1723 | n_gen =    311, tg =  19.15 t/s, tg_3s =  22.60 t/s
rag-backend      | info: System.Net.Http.HttpClient.IRagService.LogicalHandler[100]
rag-backend      |       Start processing HTTP request GET http://rag:8000/health
rag-backend      | info: System.Net.Http.HttpClient.IRagService.ClientHandler[100]
rag-backend      |       Sending HTTP request GET http://rag:8000/health
rag-rag-service  | INFO:     172.16.1.5:57922 - "GET /health HTTP/1.1" 200 OK
rag-backend      | info: System.Net.Http.HttpClient.IRagService.ClientHandler[101]
rag-backend      |       Received HTTP response headers after 7.3386ms - 200
rag-backend      | info: System.Net.Http.HttpClient.IRagService.LogicalHandler[101]
rag-backend      |       End processing HTTP request after 10.9126ms - 200
rag-ollama       | slot print_timing: id  0 | task 1723 | n_gen =    358, tg =  18.58 t/s, tg_3s =  15.53 t/s
rag-rag-service  | INFO:     127.0.0.1:60646 - "GET /health HTTP/1.1" 200 OK
rag-ollama       | slot print_timing: id  0 | task 1723 | n_gen =    385, tg =  17.28 t/s, tg_3s =   8.96 t/s
rag-ollama       | slot print_timing: id  0 | task 1723 | n_gen =    398, tg =  15.73 t/s, tg_3s =   4.31 t/s
rag-ollama       | slot print_timing: id  0 | task 1723 | prompt eval time =     910.67 ms /     1 tokens (  910.67 ms per token,     1.10 tokens per second)
rag-ollama       | slot print_timing: id  0 | task 1723 |        eval time =   25649.10 ms /   406 tokens (   63.33 ms per token,    15.79 tokens per second)
rag-ollama       | slot print_timing: id  0 | task 1723 |       total time =   26559.77 ms /   407 tokens
rag-ollama       | slot print_timing: id  0 | task 1723 |    graphs reused =       2097
rag-ollama       | slot      release: id  0 | task 1723 | stop processing: n_tokens = 2040, truncated = 0
rag-ollama       | srv  update_slots: all slots are idle
rag-ollama       | [GIN] 2026/09/19 - 16:58:04 | 200 | 26.605169117s |      172.16.1.4 | POST     "/v1/chat/completions"
rag-rag-service  | 2026-09-19 16:58:04,983 INFO app.api.routes: /chat completado en 163.8s
rag-web          | 172.16.1.7 - - [19/Sep/2026:16:58:04 +0000] "POST /api/conversations/0555275f-df7d-45a9-82dd-dd4f1b77ab41/messages HTTP/1.1" 200 23827 "https://186-240-150-93.nip.io/" "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36" "95.62.214.100"
rag-web          | 172.16.1.7 - - [19/Sep/2026:16:58:05 +0000] "GET /api/conversations HTTP/1.1" 200 290 "https://186-240-150-93.nip.io/" "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36" "95.62.214.100"
rag-web          | 172.16.1.7 - - [19/Sep/2026:16:58:05 +0000] "GET /api/conversations/0555275f-df7d-45a9-82dd-dd4f1b77ab41/messages HTTP/1.1" 200 7406 "https://186-240-150-93.nip.io/" "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36" "95.62.214.100"
rag-rag-service  | INFO:     127.0.0.1:58010 - "GET /health HTTP/1.1" 200 OK
rag-backend      | info: System.Net.Http.HttpClient.IRagService.LogicalHandler[100]
rag-backend      |       Start processing HTTP request GET http://rag:8000/health
rag-backend      | info: System.Net.Http.HttpClient.IRagService.ClientHandler[100]
rag-backend      |       Sending HTTP request GET http://rag:8000/health
rag-rag-service  | INFO:     172.16.1.5:46800 - "GET /health HTTP/1.1" 200 OK
rag-backend      | info: System.Net.Http.HttpClient.IRagService.ClientHandler[101]
rag-backend      |       Received HTTP response headers after 4.2311ms - 200
rag-backend      | info: System.Net.Http.HttpClient.IRagService.LogicalHandler[101]
rag-backend      |       End processing HTTP request after 4.3276ms - 200
rag-postgres     | 2026-09-19 16:58:29.346 UTC [27] LOG:  checkpoint starting: time
rag-rag-service  | INFO:     127.0.0.1:50052 - "GET /health HTTP/1.1" 200 OK
rag-postgres     | 2026-09-19 16:58:31.178 UTC [27] LOG:  checkpoint complete: wrote 19 buffers (0.1%); 0 WAL file(s) added, 0 removed, 0 recycled; write=1.810 s, sync=0.006 s, total=1.833 s; sync files=13, longest=0.004 s, average=0.001 s; distance=67 kB, estimate=746 kB; lsn=0/30F21B8, redo lsn=0/30F2180