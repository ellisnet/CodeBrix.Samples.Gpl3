;; a scheme probe
(define (add-two x)
  "doc string"
  (+ x 2))
(let* ((a 1) (b #t) (c #{ c'4 #}))
  (display (list a b 'sym "str" 3.5)))
