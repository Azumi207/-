FROM python:3.12-slim-bookworm
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 FRAMEFLOW_TEAM_DATA=/data
WORKDIR /app
RUN groupadd --gid 10001 frameflow && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin frameflow \
    && mkdir /data /backups && chown 10001:10001 /data /backups && chmod 700 /data /backups
COPY requirements.txt /app/requirements.txt
RUN python -m pip install --no-cache-dir -r /app/requirements.txt
COPY server.py launcher.py healthcheck.py /app/
COPY web/ /app/web/
COPY docker-entrypoint.sh /app/docker-entrypoint.sh
RUN chmod 755 /app/docker-entrypoint.sh
USER 10001:10001
EXPOSE 8765
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=5 CMD ["python", "/app/healthcheck.py"]
ENTRYPOINT ["/app/docker-entrypoint.sh"]
CMD ["serve"]
